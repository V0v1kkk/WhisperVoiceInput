using Akka.Actor;
using Serilog;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Structs;
using System;
using System.IO;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Services;

namespace WhisperVoiceInput.Actors;

/// <summary>
/// Actor responsible for recording audio using SoundFlow.
/// </summary>
public class AudioRecordingActor : ReceiveActor, IWithUnboundedStash
{
    private readonly AppSettings _settings;
    private readonly ILogger _logger;
    private readonly SoundFlowAudioService _audioService;

    private const int SampleRate = 44100;

    private AudioCaptureDevice? _captureDevice;
    private Recorder? _recorder;
    private string? _currentFilePath;
    private ICancelable? _timeoutCancelable;

    public IStash Stash { get; set; } = null!;

    public AudioRecordingActor(AppSettings settings, ILogger logger, SoundFlowAudioService audioService)
    {
        _settings = settings;
        _logger = logger;
        _audioService = audioService;

        ReadyToRecord();
    }

    private void ReadyToRecord()
    {
        Receive<RecordCommand>(_ =>
        {
            try
            {
                _logger.Information("Starting audio recording");
                StartRecording();
                    
                // Transition to Recording state
                Become(Recording);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to start recording");
                    
                // Self-tell for retry after restart
                Self.Tell(new RecordCommand());
                throw;
            }
        });

        // Ignore stop command in this state
        ReceiveAny(msg => _logger.Warning("Received unexpected message in ReadyToRecord state: {MessageType}", msg.GetType().Name));
    }

    private void Recording()
    {
        Receive<RecordingTimeout>(_ =>
        {
            _logger.Error("Recording timeout reached, stopping and failing actor");
            CleanupRecording();
            if (!_settings.KeepLastRecording)
                TryDeleteFile(_currentFilePath);
            throw new UserConfiguredTimeoutException("Recording exceeded configured timeout");
        });

        Receive<StopRecordingCommand>(_ =>
        {
            try
            {
                _logger.Information("Stopping audio recording");
                string audioFile = StopRecording();

                // Send result and return to initial state
                Sender.Tell(new AudioRecordedEvent(audioFile));
                Become(ReadyToRecord);
                Stash.UnstashAll();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to stop recording, returning to ready state");

                if (_currentFilePath != null && !_settings.KeepLastRecording)
                {
                    TryDeleteFile(_currentFilePath);
                }

                throw;
            }
        });

        // Queue record commands for when we return to ready state
        Receive<RecordCommand>(_ =>
        {
            Stash.Stash();
            _logger.Information("Stashed RecordCommand while Recording");
        });

        // Ignore other messages
        ReceiveAny(msg => _logger.Warning("Received unexpected message in Recording state: {MessageType}", msg.GetType().Name));
    }

    private void StartRecording()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "WhisperVoiceInput");
        Directory.CreateDirectory(tempFolder);

        string formatId = _settings.UseWavFormat ? "wav" : "mp3";
        _currentFilePath = Path.Combine(tempFolder, $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.{formatId}");

        var format = new AudioFormat
        {
            SampleRate = SampleRate,
            Channels = 1,
            Format = SampleFormat.F32,
            Layout = ChannelLayout.Mono
        };

        DeviceInfo? deviceInfo = null;
        if (!string.IsNullOrWhiteSpace(_settings.PreferredCaptureDevice))
        {
            deviceInfo = _audioService.FindDeviceByName(_settings.PreferredCaptureDevice);
            if (deviceInfo == null)
            {
                _logger.Warning("Preferred capture device '{Device}' not found, falling back to system default",
                    _settings.PreferredCaptureDevice);
            }
        }

        _captureDevice = _audioService.Engine.InitializeCaptureDevice(deviceInfo, format);
        _recorder = new Recorder(_captureDevice, _currentFilePath, formatId);

        _captureDevice.Start();

        var result = _recorder.StartRecording();
        if (!result.IsSuccess)
        {
            _captureDevice.Stop();
            _captureDevice.Dispose();
            _captureDevice = null;
            _recorder.Dispose();
            _recorder = null;
            throw new InvalidOperationException($"Failed to start recording: {result.Error?.Message}");
        }

        _logger.Information("Recording audio in {Format} format to {FilePath}", formatId.ToUpperInvariant(), _currentFilePath);
        Context.Parent.Tell(new RecordingStartedEvent(_currentFilePath));
        ScheduleTimeoutIfEnabled();
    }

    private string StopRecording()
    {
        if (_currentFilePath == null || _recorder == null || _captureDevice == null)
        {
            throw new InvalidOperationException("No recording in progress");
        }

        // Stop the device FIRST — ma_device_stop() is synchronous and waits
        // for any in-flight native callback to complete before returning.
        _captureDevice.Stop();

        var stopResult = _recorder.StopRecording();
        if (stopResult.IsFailure)
        {
            _logger.Warning("Recorder stop reported failure: {Error}", stopResult.Error?.Message);
        }

        _captureDevice.Dispose();
        _captureDevice = null;

        _recorder.Dispose();
        _recorder = null;

        CancelTimeout();

        _logger.Information("Recording completed successfully");
        return _currentFilePath;
    }

    private void CleanupRecording()
    {
        try
        {
            if (_captureDevice != null)
            {
                _captureDevice.Stop();
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error stopping capture device during cleanup");
        }

        try
        {
            if (_recorder != null)
            {
                _recorder.StopRecording();
                _recorder.Dispose();
                _recorder = null;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error stopping recorder during cleanup");
        }

        try
        {
            if (_captureDevice != null)
            {
                _captureDevice.Dispose();
                _captureDevice = null;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error disposing capture device during cleanup");
        }

        CancelTimeout();
    }

    private void TryDeleteFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.Information("Deleted audio file due to failure/timeout: {FilePath}", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to delete audio file after failure/timeout: {FilePath}", filePath);
        }
    }

    protected override void PreRestart(Exception reason, object message)
    {
        _logger.Warning(reason, "AudioRecordingActor is restarting due to an exception");
        CleanupRecording();
        base.PreRestart(reason, message);
    }

    protected override void PostStop()
    {
        CleanupRecording();
        base.PostStop();
    }

    private void ScheduleTimeoutIfEnabled()
    {
        CancelTimeout();
        if (_settings.RecordingTimeoutMinutes > 0)
        {
            var due = TimeSpan.FromMinutes(_settings.RecordingTimeoutMinutes);
            _timeoutCancelable = Context.System.Scheduler.ScheduleTellOnceCancelable(due, Self, new RecordingTimeout(), Self);
            _logger.Information("Scheduled recording timeout in {Minutes} minutes", _settings.RecordingTimeoutMinutes);
        }
    }

    private void CancelTimeout()
    {
        try
        {
            _timeoutCancelable?.Cancel();
        }
        catch
        {
            _logger.Warning("Failed to cancel recording timeout");
        }
        _timeoutCancelable = null;
    }
}
