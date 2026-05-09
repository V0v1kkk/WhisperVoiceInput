using System.Threading.Tasks;
using WhisperVoiceInput.Abstractions;

namespace WhisperVoiceInput.Tests.TestDoubles;

public class MockWaylandInputMethodClient : IWaylandInputMethodClient
{
    public bool IsAvailable { get; set; }
    public bool IsActive { get; set; }
    public bool CommitResult { get; set; }
    public string? LastCommittedText { get; private set; }
    public int CommitCallCount { get; private set; }

    public void Start() { }
    public void Stop() { }

    public Task<bool> CommitTextAsync(string text)
    {
        LastCommittedText = text;
        CommitCallCount++;
        return Task.FromResult(CommitResult);
    }

    public void Dispose() { }
}
