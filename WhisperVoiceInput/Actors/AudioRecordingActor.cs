using Akka.Actor;
using Serilog;
using NAudio.Lame;
using NAudio.Wave;
using OpenTK.Audio.OpenAL;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.Actors;

/// <summary>
/// Actor responsible for recording audio.
/// </summary>
public class AudioRecordingActor : ReceiveActor, IWithUnboundedStash
{
    private readonly AppSettings _settings;
    private readonly ILogger _logger;
        
    private const int SampleRate = 44100;
    private ALCaptureDevice _captureDevice;
    private string? _currentFilePath;
    private CancellationTokenSource? _recordingCancellation;
    private Task? _recordingTask;
        
    // Required by IWithUnboundedStash
    public IStash Stash { get; set; } = null!;
        
    public AudioRecordingActor(AppSettings settings, ILogger logger)
    {
        _settings = settings;
        _logger = logger;
        _captureDevice = ALCaptureDevice.Null;
            
        // Initial state - ready to record
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
        Receive<StopRecordingCommand>(_ => 
        {
            try
            {
                _logger.Information("Stopping audio recording");
                string audioFile = StopRecording();
                    
                // Send result and return to initial state
                Sender.Tell(new AudioRecordedEvent(audioFile));
                Become(ReadyToRecord);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to stop recording, returning to ready state");
                    
                // Even if stopping fails, we'll send an event with the current file path
                // and return to ready state
                if (_currentFilePath != null)
                {
                    Sender.Tell(new AudioRecordedEvent(_currentFilePath));
                }
                else
                {
                    // If we don't have a path, something went really wrong
                    // Let supervision handle this
                    throw;
                }
                    
                Become(ReadyToRecord);
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
        _recordingCancellation = new CancellationTokenSource();

        // Create temporary file for recording
        var tempFolder = Path.Combine(Path.GetTempPath(), "WhisperVoiceInput");
        Directory.CreateDirectory(tempFolder);
        _currentFilePath = Path.Combine(tempFolder, $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp3");

        // Choose capture device: preferred from settings or system default
        string preferredDevice = _settings.PreferredCaptureDevice;
        string defaultDevice = ALC.GetString(ALDevice.Null, AlcGetString.CaptureDefaultDeviceSpecifier);
        string deviceName = string.IsNullOrWhiteSpace(preferredDevice) ? defaultDevice : preferredDevice;

        // Try open preferred or default device first
        _captureDevice = ALC.CaptureOpenDevice(deviceName, SampleRate, ALFormat.Mono16, 4096);

        if (_captureDevice.Equals(ALCaptureDevice.Null))
        {
            // Fallback to system default if preferred failed or device not available
            if (!string.Equals(deviceName, defaultDevice, StringComparison.Ordinal))
            {
                _logger.Warning("Preferred capture device not available: {Device}. Falling back to default: {DefaultDevice}", deviceName, defaultDevice);
                _captureDevice = ALC.CaptureOpenDevice(defaultDevice, SampleRate, ALFormat.Mono16, 4096);
            }

            if (_captureDevice.Equals(ALCaptureDevice.Null))
            {
                throw new InvalidOperationException($"Failed to open capture device. Tried: '{deviceName}', fallback: '{defaultDevice}'");
            }
        }

        _logger.Information("Starting audio recording to {FilePath}", _currentFilePath);

        // Start recording
        ALC.CaptureStart(_captureDevice);

        // Start a task to collect samples
        _recordingTask = Task.Run(async () =>
        {
            try
            {
                using var writer = new LameMP3FileWriter(
                    _currentFilePath,
                    new WaveFormat(SampleRate, 16, 1),
                    128);

                var buffer = new short[4096];
                var byteBuffer = new byte[buffer.Length * 2]; // 16-bit samples

                while (!_recordingCancellation.Token.IsCancellationRequested)
                {
                    // Get number of available samples
                    ALC.GetInteger(_captureDevice, AlcGetInteger.CaptureSamples, 1, out var samples);

                    if (samples >= buffer.Length)
                    {
                        unsafe
                        {
                            fixed (short* pBuffer = buffer)
                            {
                                ALC.CaptureSamples(_captureDevice, (IntPtr)pBuffer, buffer.Length);
                            }
                        }

                        // Convert short array to byte array
                        Buffer.BlockCopy(buffer, 0, byteBuffer, 0, byteBuffer.Length);

                        await writer.WriteAsync(byteBuffer, 0, byteBuffer.Length);
                    }
                    else
                    {
                        await Task.Delay(10);
                    }
                }

                _logger.Information("Recording completed successfully");
                await writer.FlushAsync();
            }
            catch (TaskCanceledException)
            {
                _logger.Information("Recording was cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during recording");
            }
            finally
            {
                CleanupRecording();
            }
        }, _recordingCancellation.Token);
    }

    private string StopRecording()
    {
        if (_currentFilePath == null || _recordingCancellation == null)
        {
            throw new InvalidOperationException("No recording in progress");
        }
            
        _recordingCancellation.Cancel();
            
        // Wait for the recording task to complete
        try 
        {
            _recordingTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error waiting for recording task to complete");
        }
            
        return _currentFilePath;
    }
        
    private void CleanupRecording()
    {
        if (!_captureDevice.Equals(ALCaptureDevice.Null))
        {
            ALC.CaptureStop(_captureDevice);
            ALC.CaptureCloseDevice(_captureDevice);
            _captureDevice = ALCaptureDevice.Null;
        }
    }

    protected override void PreRestart(Exception reason, object message)
    {
        _logger.Warning(reason, "AudioRecordingActor is restarting due to an exception");
        CleanupRecording();
        _recordingCancellation?.Dispose();
        base.PreRestart(reason, message);
    }
        
    protected override void PostStop()
    {
        CleanupRecording();
        _recordingCancellation?.Dispose();
        base.PostStop();
    }
}