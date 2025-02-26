using System;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
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
    
    private readonly BehaviorSubject<bool> _recordingInProgressSubject;

    private const int SampleRate = 44100;
    private ALCaptureDevice _captureDevice;
    private string? _currentFilePath;
    private TaskCompletionSource<string>? _recordingCompletion;
    private CancellationTokenSource? _recordingCancellation;
    private bool _isDisposed;
    

    public IObservable<bool> RecordingInProgressObservable { get; }

    public AudioRecordingService(ILogger logger)
    {
        _logger = logger;
        _captureDevice = ALCaptureDevice.Null;
        
        _recordingInProgressSubject = new BehaviorSubject<bool>(false);
        RecordingInProgressObservable = _recordingInProgressSubject.AsObservable();
    }

    public Task<string> StartRecordingAsync()
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
            _captureDevice = ALC.CaptureOpenDevice(deviceName, SampleRate, ALFormat.Mono16, 4096);

            if (_captureDevice.Equals(ALCaptureDevice.Null))
            {
                throw new InvalidOperationException($"Failed to open capture device: {deviceName}");
            }

            _logger.Information("Starting audio recording to {FilePath}", _currentFilePath);
            _recordingInProgressSubject.OnNext(true);

            // Start recording
            ALC.CaptureStart(_captureDevice);

            // Start a task to collect samples
            _ = Task.Run(async () =>
            {
                try
                {
                    var writer = new LameMP3FileWriter(
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
                    await writer.DisposeAsync();
                    _recordingCompletion?.TrySetResult(_currentFilePath);
                }
                catch (TaskCanceledException)
                {
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
                    _recordingInProgressSubject.OnNext(false);
                }
            }, _recordingCancellation.Token);

            return _recordingCompletion.Task;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start audio recording");
            _recordingInProgressSubject.OnNext(false);
            throw;
        }
    }

    public Task<string> StopRecording()
    {
        if (_recordingCompletion == null)
        {
            throw new InvalidOperationException("No recording in progress");
        }
        
        _recordingCancellation?.Cancel();
        return _recordingCompletion.Task;
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