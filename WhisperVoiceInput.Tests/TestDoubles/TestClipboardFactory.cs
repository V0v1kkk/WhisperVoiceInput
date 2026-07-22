using Avalonia.Input.Platform;
using Moq;

namespace WhisperVoiceInput.Tests.TestDoubles;

/// <summary>
/// Creates Moq proxies for Avalonia's non-implementable IClipboard interface.
/// </summary>
public static class TestClipboardFactory
{
    public static (IClipboard Clipboard, Mock<IClipboard> Mock) Create()
    {
        var mock = new Mock<IClipboard>(MockBehavior.Strict);
        mock.Setup(c => c.SetDataAsync(It.IsAny<Avalonia.Input.IAsyncDataTransfer?>()))
            .Returns(Task.CompletedTask);
        mock.Setup(c => c.ClearAsync()).Returns(Task.CompletedTask);
        mock.Setup(c => c.FlushAsync()).Returns(Task.CompletedTask);
        mock.Setup(c => c.TryGetDataAsync())
            .ReturnsAsync((Avalonia.Input.IAsyncDataTransfer?)null);
        return (mock.Object, mock);
    }

    public static (IClipboard Clipboard, Mock<IClipboard> Mock) CreateThrowing(Exception exception)
    {
        var mock = new Mock<IClipboard>(MockBehavior.Strict);
        mock.Setup(c => c.SetDataAsync(It.IsAny<Avalonia.Input.IAsyncDataTransfer?>()))
            .ThrowsAsync(exception);
        return (mock.Object, mock);
    }
}
