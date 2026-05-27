using System;
using System.Linq;
using Serilog;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Codecs.FFMpeg;
using SoundFlow.Structs;

namespace WhisperVoiceInput.Services;

/// <summary>
/// Manages the SoundFlow audio engine lifetime and provides device enumeration.
/// Should be registered as a singleton and disposed on application shutdown.
/// </summary>
public sealed class SoundFlowAudioService : IDisposable
{
    private readonly ILogger _logger;

    public MiniAudioEngine Engine { get; }

    public SoundFlowAudioService(ILogger logger)
    {
        _logger = logger.ForContext<SoundFlowAudioService>();

        Engine = new MiniAudioEngine();
        Engine.RegisterCodecFactory(new FFmpegCodecFactory());

        _logger.Information("SoundFlow audio engine initialized. Backend: {Backend}", Engine.ActiveBackend);
    }

    public DeviceInfo[] GetCaptureDevices()
    {
        try
        {
            Engine.UpdateAudioDevicesInfo();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh audio device list");
        }

        return Engine.CaptureDevices;
    }

    public DeviceInfo? FindDeviceByName(string name)
    {
        var devices = Engine.CaptureDevices;
        var match = devices.FirstOrDefault(d =>
            string.Equals(d.Name, name, StringComparison.Ordinal));

        if (match.Name != null)
            return match;

        _logger.Warning("Capture device '{DeviceName}' not found among {Count} available devices", name, devices.Length);
        return null;
    }

    public void Dispose()
    {
        Engine.Dispose();
        _logger.Information("SoundFlow audio engine disposed");
    }
}
