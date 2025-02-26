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

    public CommandSocketListener(
        ILogger logger,
        string socketPath,
        Func<Task> startRecordingCallback)
    {
        _logger = logger;
        _socketPath = socketPath;
        _startRecordingCallback = startRecordingCallback;
        _cancellationTokenSource = new CancellationTokenSource();
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
        try
        {
            // Ensure the socket file doesn't exist
            if (File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
            }

            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(_socketPath));
            socket.Listen(10);

            _logger.Information("Command socket listener started at {SocketPath}", _socketPath);

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
        catch (Exception ex)
        {
            _logger.Error(ex, "Command socket listener failed");
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