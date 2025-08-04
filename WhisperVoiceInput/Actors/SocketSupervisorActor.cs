using System;
using Akka.Actor;
using Serilog;

namespace WhisperVoiceInput.Actors;

/// <summary>
/// Simple supervisor actor for socket listener
/// </summary>
public class SocketSupervisorActor : ReceiveActor
{
    private readonly ILogger _logger;

    public SocketSupervisorActor(ILogger logger)
    {
        _logger = logger.ForContext<SocketSupervisorActor>();
        _logger.Information("SocketSupervisorActor created");
    }

    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new OneForOneStrategy(
            maxNrOfRetries: 3,
            withinTimeRange: TimeSpan.FromMinutes(1),
            localOnlyDecider: ex =>
            {
                _logger.Warning(ex, "Socket listener failed, restarting");
                return Directive.Restart;
            });
    }
}