using Akka.Actor;
using Akka.Configuration;
using Akka.Logger.Serilog;
using Serilog;
using System;
using System.Reactive.Linq;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Actors;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Messages;

namespace WhisperVoiceInput.Services;

/// <summary>
/// Manages the Akka.NET actor system lifecycle and implements all actor-related interfaces
/// </summary>
public class ActorSystemManager : IRecordingToggler, IStateObservableFactory, IDisposable
{
    private readonly ILogger _logger;
    private ActorSystem? _actorSystem;
    private bool _disposed;
    private IDisposable? _settingsUpdateSubscription;

    // Core actor references
    public IActorRef? MainOrchestratorActor { get; private set; }
    public IActorRef? ObserverActor { get; private set; }
    public IActorRef? SocketListenerActor { get; private set; }

    public ActorSystemManager(ILogger logger)
    {
        _logger = logger.ForContext<ActorSystemManager>();
    }

    public void Initialize(ISettingsService settingsService, RetryPolicySettings retrySettings, 
        IActorPropsFactory propsFactory, IClipboardService clipboardService)
    {
        try
        {
            _logger.Information("Initializing actor system");

            // Configure Akka with Serilog
            var config = ConfigurationFactory.ParseString(@"
                    akka {
                        loggers = [""Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog""]
                        loglevel = INFO
                        
                        actor {
                            debug {
                                receive = on
                                autoreceive = on
                                lifecycle = on
                                event-stream = on
                            }
                        }
                    }
                ");

            _actorSystem = ActorSystem.Create("WhisperVoiceInput", config);

            // Create ObserverActor first (needed by MainOrchestrator)
            ObserverActor = _actorSystem.ActorOf(
                propsFactory.CreateObserverActorProps(),
                "observer");

            // Create MainOrchestratorActor
            MainOrchestratorActor = _actorSystem.ActorOf(
                Props.Create(() => new MainOrchestratorActor(
                    propsFactory,
                    clipboardService,
                    _logger.ForContext<MainOrchestratorActor>(),
                    settingsService.CurrentSettings,
                    retrySettings,
                    ObserverActor)),
                "main-orchestrator");
                
            _settingsUpdateSubscription = settingsService.Settings
                .DistinctUntilChanged()
                .Subscribe(newSettings =>
                {
                    _logger.Information("Updating settings in actor system");
                    MainOrchestratorActor?.Tell(new UpdateSettingsCommand(newSettings));
                }); 
                
                
            // Create SocketListenerActor with its own supervisor (Linux only)
            if (OperatingSystem.IsLinux())
            {
                var socketPath = "/tmp/WhisperVoiceInput/pipe";
                var socketSupervisor = _actorSystem.ActorOf(
                    Props.Create(() => new SocketSupervisorActor(_logger.ForContext<SocketSupervisorActor>())),
                    "socket-supervisor");

                SocketListenerActor = _actorSystem.ActorOf(
                    Props.Create(() => new SocketListenerActor(
                        _logger.ForContext<SocketListenerActor>(),
                        socketPath,
                        MainOrchestratorActor)),
                    "socket-listener");

                // Start listening
                SocketListenerActor.Tell(new StartListeningCommand());
            }

            _logger.Information("Actor system initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize actor system");
            throw;
        }
    }
        

    #region IRecordingToggler Implementation

    public void ToggleRecording()
    {
        _logger.Information("Toggling recording via actor system");
        MainOrchestratorActor?.Tell(new ToggleCommand());
    }

    #endregion

    #region IStateObservableFactory Implementation

    public IObservable<StateUpdatedEvent> GetStateObservable()
    {
        if (ObserverActor == null)
        {
            throw new InvalidOperationException("Actor system not initialized. Call Initialize() first.");
        }

        _logger.Debug("Requesting state observable from ObserverActor");
            
        // Ask the ObserverActor for its observable
        // This will be implemented in ObserverActor to return an IObservable
        var response = ObserverActor.Ask<StateObservableResult>(new GetStateObservableCommand()).GetAwaiter().GetResult();
        return response.Observable;
    }

    #endregion

    #region IDisposable Implementation

    public void Dispose()
    {
        if (_disposed) return;

        _logger.Information("Shutting down actor system");

        try
        {
            _actorSystem?.Terminate().Wait(TimeSpan.FromSeconds(10));
            _logger.Information("Actor system shut down successfully");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error during actor system shutdown");
        }
        finally
        {
            _actorSystem?.Dispose();
            _settingsUpdateSubscription?.Dispose();
            _disposed = true;
        }
    }

    #endregion
}