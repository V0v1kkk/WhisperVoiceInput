using System;
using System.Threading.Tasks;

namespace WhisperVoiceInput.Abstractions;

public interface IWaylandInputMethodClient : IDisposable
{
    /// <summary>
    /// Whether the Wayland IME connection is currently established and the protocol is supported.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Whether a text field currently has focus (compositor sent 'activate').
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Connect to Wayland display and register as an IME client.
    /// Safe to call multiple times; does nothing if already started.
    /// If the environment is not suitable (no Wayland, no protocol support), logs and stays stopped.
    /// </summary>
    void Start();

    /// <summary>
    /// Disconnect and release all native resources.
    /// Safe to call multiple times. After Stop(), Start() can be called again.
    /// </summary>
    void Stop();

    /// <summary>
    /// Attempt to commit text via the Wayland IME protocol.
    /// Returns true if the text was committed, false if not available or no active text field.
    /// </summary>
    Task<bool> CommitTextAsync(string text);
}
