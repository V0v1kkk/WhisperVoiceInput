using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace WhisperVoiceInput.Services;

public class CommandSocketListener : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _socketPath;
    private readonly Func<Task> _startRecordingCallback;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Task? _listenerTask;
    private bool _isDisposed;
    
    // Retry configuration
    private readonly int _maxRetries;
    private readonly TimeSpan _retryDelay;
    private const int DefaultRetryDelayMs = 1000; // 1 second delay between retries

    public CommandSocketListener(
        ILogger logger,
        string socketPath,
        Func<Task> startRecordingCallback,
        int maxRetries = -1, // -1 means unlimited retries
        int retryDelayMs = DefaultRetryDelayMs)
    {
        _logger = logger.ForContext<CommandSocketListener>();
        _socketPath = socketPath;
        _startRecordingCallback = startRecordingCallback;
        _cancellationTokenSource = new CancellationTokenSource();
        _maxRetries = maxRetries;
        _retryDelay = TimeSpan.FromMilliseconds(retryDelayMs);
    }

    public void Start()
    {
        if (_listenerTask != null)
        {
            throw new InvalidOperationException("Listener is already running");
        }

        _listenerTask = Task.Run(ListenForCommandsAsync);
    }

    private async Task ListenForCommandsAsync()
    {
        int retryCount = 0;
        bool socketCreated = false;
        Socket? socket = null;

        while (!socketCreated && (_maxRetries < 0 || retryCount <= _maxRetries) && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                // Ensure the socket file doesn't exist
                if (File.Exists(_socketPath))
                {
                    File.Delete(_socketPath);
                }

                // Ensure the directory exists
                string socketDirectory = Path.GetDirectoryName(_socketPath);
                if (!string.IsNullOrEmpty(socketDirectory) && !Directory.Exists(socketDirectory))
                {
                    Directory.CreateDirectory(socketDirectory);
                    _logger.Information("Created socket directory: {SocketDirectory}", socketDirectory);
                }

                socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Bind(new UnixDomainSocketEndPoint(_socketPath));
                socket.Listen(10);

                socketCreated = true;
                _logger.Information("Command socket listener started at {SocketPath} after {RetryCount} attempts",
                    _socketPath, retryCount);
            }
            catch (Exception ex)
            {
                retryCount++;
                
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    _logger.Information("Socket creation cancelled during retry");
                    break;
                }
                
                if (_maxRetries < 0 || retryCount <= _maxRetries)
                {
                    _logger.Warning(ex, "Failed to create socket (attempt {RetryCount}), retrying in {RetryDelay}ms...",
                        retryCount, _retryDelay.TotalMilliseconds);
                    
                    // Dispose of the socket if it was created
                    socket?.Dispose();
                    socket = null;
                    
                    try
                    {
                        // Wait before retrying
                        await Task.Delay(_retryDelay, _cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.Information("Socket creation cancelled during delay");
                        break;
                    }
                }
                else
                {
                    _logger.Error(ex, "Command socket listener failed after {RetryCount} attempts", retryCount);
                    return; // Exit the method after max retries
                }
            }
        }

        // Check if we exited the loop due to cancellation
        if (_cancellationTokenSource.Token.IsCancellationRequested)
        {
            _logger.Information("Socket creation cancelled");
            socket?.Dispose();
            return;
        }
        
        // Check if we failed to create the socket
        if (socket == null || !socketCreated)
        {
            _logger.Error("Failed to create socket after retry attempts");
            socket?.Dispose();
            return;
        }

        try
        {
            using (socket) // Ensure socket is disposed when done
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        using var client = await socket.AcceptAsync();
                        _ = HandleClientAsync(client); // Fire and forget
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error accepting client connection");
                    }
                }
            }
        }
        finally
        {
            try
            {
                if (File.Exists(_socketPath))
                {
                    File.Delete(_socketPath);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to delete socket file");
            }
        }
    }

    private async Task HandleClientAsync(Socket client)
    {
        try
        {
            var buffer = ArrayPool<byte>.Shared.Rent(1024);
            try
            {
                var received = await client.ReceiveAsync(buffer, SocketFlags.None);
                if (received > 0)
                {
                    var command = Encoding.UTF8.GetString(buffer, 0, received).Trim();
                    _logger.Information("Received command: {Command}", command);

                    if (command.Equals("transcribe_toggle", StringComparison.OrdinalIgnoreCase))
                    {
                        await _startRecordingCallback();
                        await client.SendAsync(Encoding.UTF8.GetBytes("OK"), SocketFlags.None);
                    }
                    else
                    {
                        await client.SendAsync(Encoding.UTF8.GetBytes("Unknown command"), SocketFlags.None);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling client connection");
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();

            try
            {
                if (File.Exists(_socketPath))
                {
                    File.Delete(_socketPath);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to delete socket file during disposal");
            }
        }
    }
}