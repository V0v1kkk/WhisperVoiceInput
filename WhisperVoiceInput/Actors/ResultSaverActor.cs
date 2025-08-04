using Akka.Actor;
using Serilog;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.Actors;

/// <summary>
/// Actor responsible for saving transcription results based on configured output type.
/// Contains all the existing result output logic from ApplicationViewModel.
/// </summary>
public class ResultSaverActor : ReceiveActor
{
    private readonly AppSettings _settings;
    private readonly ILogger _logger;
    private readonly IClipboardService _clipboardService;

    public ResultSaverActor(AppSettings settings, ILogger logger, IClipboardService clipboardService)
    {
        _settings = settings;
        _logger = logger;
        _clipboardService = clipboardService;

        ReceiveAsync<ResultAvailableEvent>(HandleResultAvailableEvent);
    }

    private async Task HandleResultAvailableEvent(ResultAvailableEvent evt)
    {
        try
        {
            _logger.Information("Saving result with {OutputType}", _settings.OutputType);
                
            await OutputTextAsync(evt.Text);
                
            _logger.Information("Result saved successfully");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save result");
                
            // Self-tell for retry after restart
            Self.Tell(evt);
            throw;
        }
    }

    private async Task OutputTextAsync(string text)
    {
        switch (_settings.OutputType)
        {
            case ResultOutputType.ClipboardAvaloniaApi:
                await _clipboardService.SetTextAsync(text);
                break;
                    
            case ResultOutputType.WlCopy:
                await CopyToClipboardWaylandAsync(text);
                break;
                    
            case ResultOutputType.YdotoolType:
                await TypeWithYdotoolAsync(text);
                break;
                    
            case ResultOutputType.WtypeType:
                await TypeWithWtypeAsync(text);
                break;
                    
            default:
                _logger.Warning("Unknown output type: {OutputType}", _settings.OutputType);
                break;
        }
    }

    private async Task CopyToClipboardWaylandAsync(string text)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "wl-copy",
                Arguments = text,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            await process.WaitForExitAsync();
                
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"wl-copy exited with code {process.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to copy to clipboard using wl-copy");
            throw;
        }
    }

    private async Task TypeWithYdotoolAsync(string text)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "ydotool",
                Arguments = $"type -d 1 {text}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            process.Start();
            await process.WaitForExitAsync();
                
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"ydotool exited with code {process.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to type text using ydotool");
            throw;
        }
    }

    private async Task TypeWithWtypeAsync(string text)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "wtype",
                Arguments = $"\"{text}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            process.Start();
            await process.WaitForExitAsync();
                
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"wtype exited with code {process.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to type text using wtype");
            throw;
        }
    }

    protected override void PreRestart(Exception reason, object message)
    {
        _logger.Warning(reason, "ResultSaverActor is restarting due to an exception");
        base.PreRestart(reason, message);
    }
}