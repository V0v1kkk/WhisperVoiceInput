using Akka.Actor;
using Serilog;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using WhisperVoiceInput.Messages;

namespace WhisperVoiceInput.Actors;

/// <summary>
/// Actor responsible for listening to socket commands (optional for Linux).
/// Uses PipeTo technique for handling asynchronous socket operations.
/// </summary>
public class SocketListenerActor : ReceiveActor
{
    private readonly ILogger _logger;
    private readonly string _socketPath;
    private readonly IActorRef _mainOrchestratorActor;
    private Socket? _listenSocket;
    private bool _isListening;

    public SocketListenerActor(ILogger logger, string socketPath, IActorRef mainOrchestratorActor)
    {
        _logger = logger;
        _socketPath = socketPath;
        _mainOrchestratorActor = mainOrchestratorActor;

        Receive<StartListeningCommand>(HandleStartListening);
        Receive<StopListeningCommand>(HandleStopListening);
        Receive<ClientConnected>(HandleClientConnected);
        Receive<ClientDataReceived>(HandleClientData);
        Receive<SocketError>(HandleSocketError);
    }

    private void HandleStartListening(StartListeningCommand cmd)
    {
        try
        {
            if (_isListening)
            {
                _logger.Warning("Socket listener is already running");
                return;
            }

            _logger.Information("Starting socket listener on {SocketPath}", _socketPath);
                
            // Clean up any existing socket file
            if (File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
            }

            _listenSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            var endPoint = new UnixDomainSocketEndPoint(_socketPath);
                
            _listenSocket.Bind(endPoint);
            _listenSocket.Listen(10);
            _isListening = true;

            _logger.Information("Socket listener started successfully");
                
            // Start accepting connections using PipeTo
            StartAccepting();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start socket listener");
            Self.Tell(new SocketError(ex));
        }
    }

    private void HandleStopListening(StopListeningCommand cmd)
    {
        try
        {
            _logger.Information("Stopping socket listener");
            _isListening = false;
                
            _listenSocket?.Close();
            _listenSocket?.Dispose();
            _listenSocket = null;

            // Clean up socket file
            if (File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
                _logger.Information("Cleaned up socket file {SocketPath}", _socketPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error while stopping socket listener");
        }
    }

    private void HandleClientConnected(ClientConnected connected)
    {
        try
        {
            _logger.Debug("Client connected, reading data");
                
            // Start reading data from client using PipeTo
            var readTask = ReadClientDataAsync(connected.ClientSocket);
            readTask.PipeTo(Self, 
                success: data => new ClientDataReceived(data, connected.ClientSocket),
                failure: ex => new SocketError(ex));

            // Continue accepting new connections if still listening
            if (_isListening)
            {
                StartAccepting();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling client connection");
            connected.ClientSocket?.Dispose();
        }
    }

    private void HandleClientData(ClientDataReceived data)
    {
        try
        {
            var command = data.Data.Trim();
            _logger.Debug("Received socket command: {Command}", command);

            if (command.Equals("transcribe_toggle", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Information("Forwarding toggle command to main orchestrator");
                _mainOrchestratorActor.Tell(new ToggleCommand());
            }
            else
            {
                _logger.Warning("Unknown socket command: {Command}", command);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing socket data");
        }
        finally
        {
            // Clean up client socket
            data.ClientSocket?.Dispose();
        }
    }

    private void HandleSocketError(SocketError error)
    {
        _logger.Error(error.Exception, "Socket error occurred");
            
        // If we're still supposed to be listening, try to restart
        if (_isListening && _listenSocket?.IsBound != true)
        {
            _logger.Information("Attempting to restart socket listener after error");
            Self.Tell(new StopListeningCommand());
            Self.Tell(new StartListeningCommand());
        }
    }

    private void StartAccepting()
    {
        if (_listenSocket != null && _isListening)
        {
            var acceptTask = _listenSocket.AcceptAsync();
            acceptTask.PipeTo(Self, 
                success: socket => new ClientConnected(socket),
                failure: ex => new SocketError(ex));
        }
    }

    private async Task<string> ReadClientDataAsync(Socket clientSocket)
    {
        var buffer = new byte[1024];
        var received = await clientSocket.ReceiveAsync(buffer, SocketFlags.None);
            
        if (received > 0)
        {
            return Encoding.UTF8.GetString(buffer, 0, received);
        }
            
        return string.Empty;
    }

    protected override void PreStart()
    {
        _logger.Information("SocketListenerActor starting");
        base.PreStart();
    }

    protected override void PostStop()
    {
        _logger.Information("SocketListenerActor stopping");
            
        try
        {
            _isListening = false;
            _listenSocket?.Close();
            _listenSocket?.Dispose();
                
            if (File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error during SocketListenerActor cleanup");
        }
            
        base.PostStop();
    }

    protected override void PreRestart(Exception reason, object message)
    {
        _logger.Warning(reason, "SocketListenerActor is restarting");
        base.PreRestart(reason, message);
    }
}

#region Socket Listener Internal Messages

/// <summary>
/// Message indicating a client has connected
/// </summary>
internal record ClientConnected(Socket ClientSocket);

/// <summary>
/// Message containing received data from a client
/// </summary>
internal record ClientDataReceived(string Data, Socket ClientSocket);

/// <summary>
/// Message indicating a socket error occurred
/// </summary>
internal record SocketError(Exception Exception);

#endregion