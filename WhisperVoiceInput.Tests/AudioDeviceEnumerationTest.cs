using NUnit.Framework;
using SoundFlow.Backends.MiniAudio;

namespace WhisperVoiceInput.Tests;

[TestFixture]
public class AudioDeviceEnumerationTest
{
    [Test]
    [Explicit("Manual test for enumerating audio devices on the current machine")]
    public void EnumerateAllRecordingDevices()
    {
        using var engine = new MiniAudioEngine();

        Console.WriteLine($"Active backend: {engine.ActiveBackend}");
        Console.WriteLine($"\nCapture devices ({engine.CaptureDevices.Length}):");
        foreach (var device in engine.CaptureDevices)
        {
            var defaultMarker = device.IsDefault ? " [Default]" : "";
            Console.WriteLine($"  - {device.Name}{defaultMarker}");
        }

        Console.WriteLine($"\nPlayback devices ({engine.PlaybackDevices.Length}):");
        foreach (var device in engine.PlaybackDevices)
        {
            var defaultMarker = device.IsDefault ? " [Default]" : "";
            Console.WriteLine($"  - {device.Name}{defaultMarker}");
        }
    }
}
