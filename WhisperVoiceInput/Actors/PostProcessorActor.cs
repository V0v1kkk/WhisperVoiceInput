using Akka.Actor;
using Microsoft.Extensions.AI;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.Actors;

/// <summary>
/// Actor responsible for post-processing transcribed text using AI.
/// Uses Microsoft.Extensions.AI to call OpenAI-compatible APIs.
/// </summary>
    public class PostProcessorActor : ReceiveActor
{
    private readonly AppSettings _settings;
    private readonly ILogger _logger;
    private readonly IChatClient _chatClient;
        private ICancelable? _timeoutCancelable;
        private sealed record InternalSuccess(string ProcessedText);
        private sealed record InternalFailure(Exception Exception, PostProcessCommand OriginalCommand);

    public PostProcessorActor(AppSettings settings, ILogger logger)
    {
        _settings = settings;
        _logger = logger;
        _chatClient = CreateChatClient();

        Receive<PostProcessCommand>(HandlePostProcessCommand);
        Receive<InternalSuccess>(msg => HandleInternalSuccess(msg));
        Receive<InternalFailure>(msg => HandleInternalFailure(msg));
        Receive<PostProcessingTimeout>(_ => HandleTimeout());
    }

    private IChatClient CreateChatClient()
    {
        try
        {
            // Create ChatClient using the modern Microsoft.Extensions.AI API
            string apiKey = !string.IsNullOrEmpty(_settings.PostProcessingApiKey) 
                ? _settings.PostProcessingApiKey 
                : "dummy-api-key";

            var options = new OpenAI.OpenAIClientOptions();
            if (!string.IsNullOrEmpty(_settings.PostProcessingApiUrl))
            {
                options.Endpoint = new Uri(_settings.PostProcessingApiUrl);
            }
                    
            var openAiClient = new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), options);
            var chatClient = openAiClient.GetChatClient(_settings.PostProcessingModelName);
            return chatClient.AsIChatClient();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create chat client with model {Model} and endpoint {Endpoint}", 
                _settings.PostProcessingModelName, _settings.PostProcessingApiUrl);
            throw;
        }
    }

    private void HandlePostProcessCommand(PostProcessCommand cmd)
    {
        try
        {
            _logger.Information("Starting post-processing for text with length {Length}", cmd.Text.Length);

            ScheduleTimeoutIfEnabled();
            PostProcessTextAsync(cmd.Text)
                .PipeTo(Self,
                    success: processed => new InternalSuccess(processed),
                    failure: ex => new InternalFailure(ex, cmd));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initiate post-processing");
            Self.Tell(cmd);
            throw;
        }
    }

    private void HandleInternalSuccess(InternalSuccess msg)
    {
        CancelTimeout();
        _logger.Information("Post-processing completed successfully");
        Context.Parent.Tell(new PostProcessedEvent(msg.ProcessedText));
    }

    private void HandleInternalFailure(InternalFailure msg)
    {
        CancelTimeout();
        _logger.Error(msg.Exception, "Failed to post-process text");
        Self.Tell(msg.OriginalCommand);
        throw msg.Exception;
    }

    private async Task<string> PostProcessTextAsync(string text)
    {
        // Validate input to prevent issues
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        // Use the configurable prompt from settings as the system message
        var systemMessage = new ChatMessage(ChatRole.System, _settings.PostProcessingPrompt);
            
        // User message contains only the transcribed text without special instructions
        var userMessage = new ChatMessage(ChatRole.User, text);

        var messages = new List<ChatMessage> { systemMessage, userMessage };

        try
        {
            var response = await _chatClient.GetResponseAsync(messages);
                
            if (string.IsNullOrEmpty(response?.Text))
            {
                _logger.Warning("Received empty response from AI service, returning original text");
                return text;
            }

            var processedText = response.Text.Trim();
                
            // Basic validation - if the result is significantly different in length, 
            // it might be an error, so return original
            if (processedText.Length < text.Length * 0.5 || processedText.Length > text.Length * 3)
            {
                _logger.Warning("Post-processed text length differs significantly from original (original: {OriginalLength}, processed: {ProcessedLength}), returning original text", 
                    text.Length, processedText.Length);
                return text;
            }

            return processedText;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error calling AI service for post-processing");
            throw;
        }
    }

    protected override void PreRestart(Exception reason, object message)
    {
        _logger.Warning(reason, "PostProcessorActor is restarting due to an exception");
        base.PreRestart(reason, message);
    }
        
    protected override void PostStop()
    {
        CancelTimeout();
        if (_chatClient is IDisposable disposable)
        {
            disposable.Dispose();
        }
        base.PostStop();
    }

    private void ScheduleTimeoutIfEnabled()
    {
        CancelTimeout();
        if (_settings.PostProcessingTimeoutMinutes > 0)
        {
            var due = TimeSpan.FromMinutes(_settings.PostProcessingTimeoutMinutes);
            _timeoutCancelable = Context.System.Scheduler.ScheduleTellOnceCancelable(due, Self, new PostProcessingTimeout(), Self);
            _logger.Information("Scheduled post-processing timeout in {Minutes} minutes", _settings.PostProcessingTimeoutMinutes);
        }
    }

    private void CancelTimeout()
    {
        try
        {
            _timeoutCancelable?.Cancel();
        }
        catch
        {
            _logger.Warning("Failed to cancel timeout, it may have already been triggered or cancelled");
        }
        _timeoutCancelable = null;
    }

    private void HandleTimeout()
    {
        _logger.Error("Post-processing timeout reached, failing actor");
        throw new UserConfiguredTimeoutException("Post-processing exceeded configured timeout");
    }
}