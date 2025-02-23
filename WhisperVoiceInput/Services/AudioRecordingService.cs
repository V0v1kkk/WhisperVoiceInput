using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Lame;
using NAudio.Wave;
using OpenTK.Audio.OpenAL;
using Serilog;

namespace WhisperVoiceInput.Services;

public class AudioRecordingService : IDisposable
{
    private readonly ILogger _logger;
    private readonly int _sampleRate = 44100;
    private ALCaptureDevice _captureDevice;
    private string? _currentFilePath;
    private TaskCompletionSource<string>? _recordingCompletion;
    private CancellationTokenSource? _recordingCancellation;
    private bool _isDisposed;

    public AudioRecordingService(ILogger logger)
    {
        _logger = logger;
        _captureDevice = ALCaptureDevice.Null;
    }

    public async Task<string> StartRecordingAsync()
    {
        try
        {
            _recordingCompletion = new TaskCompletionSource<string>();
            _recordingCancellation = new CancellationTokenSource();

            // Create temporary file for recording
            var tempFolder = Path.Combine(Path.GetTempPath(), "WhisperVoiceInput");
            Directory.CreateDirectory(tempFolder);
            _currentFilePath = Path.Combine(tempFolder, $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp3");

            // Get default capture device
            string deviceName = ALC.GetString(ALDevice.Null, AlcGetString.CaptureDefaultDeviceSpecifier);
            _captureDevice = ALC.CaptureOpenDevice(deviceName, _sampleRate, ALFormat.Mono16, 4096);

            if (_captureDevice.Equals(ALCaptureDevice.Null))
            {
                throw new InvalidOperationException($"Failed to open capture device: {deviceName}");
            }

            _logger.Information("Starting audio recording to {FilePath}", _currentFilePath);

            // Start recording
            ALC.CaptureStart(_captureDevice);

            // Start a task to collect samples
            _ = Task.Run(async () =>
            {
                try
                {
                    using var writer = new LameMP3FileWriter(
                        _currentFilePath,
                        new WaveFormat(_sampleRate, 16, 1),
                        128);

                    var buffer = new short[4096];
                    var byteBuffer = new byte[buffer.Length * 2]; // 16-bit samples

                    while (!_recordingCancellation.Token.IsCancellationRequested)
                    {
                        // Get number of available samples
                        int samples = 0;
                        ALC.GetInteger(_captureDevice, AlcGetInteger.CaptureSamples, 1, out samples);
                        
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
                    _recordingCompletion?.TrySetResult(_currentFilePath);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during recording");
                    _recordingCompletion?.TrySetException(ex);
                }
                finally
                {
                    if (!_captureDevice.Equals(ALCaptureDevice.Null))
                    {
                        ALC.CaptureStop(_captureDevice);
                        ALC.CaptureCloseDevice(_captureDevice);
                        _captureDevice = ALCaptureDevice.Null;
                    }
                }
            }, _recordingCancellation.Token);

            // Auto-stop after 30 seconds
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), _recordingCancellation.Token);
                    if (!_recordingCancellation.Token.IsCancellationRequested)
                    {
                        await StopRecordingAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation, ignore
                }
            });

            return await _recordingCompletion.Task;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start audio recording");
            throw;
        }
    }

    public async Task StopRecordingAsync()
    {
        try
        {
            _logger.Information("Stopping audio recording");
            _recordingCancellation?.Cancel();
            
            if (_recordingCompletion != null)
            {
                await _recordingCompletion.Task;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error stopping recording");
            throw;
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            
            if (!_captureDevice.Equals(ALCaptureDevice.Null))
            {
                ALC.CaptureStop(_captureDevice);
                ALC.CaptureCloseDevice(_captureDevice);
                _captureDevice = ALCaptureDevice.Null;
            }

            _recordingCancellation?.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}