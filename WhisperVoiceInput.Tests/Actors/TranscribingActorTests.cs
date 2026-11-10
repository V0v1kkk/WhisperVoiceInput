using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NUnit.Framework;
using OpenAI;
using WhisperVoiceInput.Actors;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Tests.TestBase;

#pragma warning disable MEAI001

namespace WhisperVoiceInput.Tests.Actors;

[TestFixture]
public class TranscribingActorTests : AkkaTestBase
{
    [TestCase(true, "")]
    [TestCase(true, "   ")]
    [TestCase(true, "WhisperVoiceInput, LiteLLM")]
    [TestCase(false, "")]
    [TestCase(false, "WhisperVoiceInput, LiteLLM")]
    public void Should_Request_Json_Only_When_Enabled_And_Preserve_Transcription_Options(bool useJson, string prompt)
    {
        var settings = TestSettings with
        {
            Model = "whisper",
            Language = "ru",
            Prompt = prompt,
            UseJsonTranscriptionResponse = useJson,
            KeepLastRecording = true
        };
        using var handler = new TranscriptionResponseHandler();
        using var http = new HttpClient(handler);
        var openAiClient = new OpenAIClient(new ApiKeyCredential("test-key"), new OpenAIClientOptions
        {
            Endpoint = new Uri("http://transcription.test/v1"),
            Transport = new HttpClientPipelineTransport(http),
            RetryPolicy = new ClientRetryPolicy(0)
        });
        using var speechToTextClient = openAiClient.GetAudioClient(settings.Model).AsISpeechToTextClient();
        var actor = ActorOfAsTestActorRef<TranscribingActor>(
            Props.Create(() => new TranscribingActor(settings, Logger, speechToTextClient))
                .WithDispatcher(CallingThreadDispatcher.Id),
            TestActor);
        var audioPath = CreateTempAudioFile();

        try
        {
            actor.Tell(new TranscribeCommand(audioPath));

            ExpectMsg<TranscriptionCompletedEvent>(TimeSpan.FromSeconds(5))
                .Text.Should().Be("Проверка совместимости.");
            handler.RequestCount.Should().Be(1);
            handler.RequestMethod.Should().Be(HttpMethod.Post);
            handler.RequestPath.Should().Be("/v1/audio/transcriptions");
            handler.RequestContentType.Should().Be("multipart/form-data");
            handler.GetField("response_format").Should().Be(useJson ? "json" : null);
            handler.GetField("model").Should().Be("whisper");
            handler.GetField("language").Should().Be("ru");
            handler.GetField("prompt").Should().Be(string.IsNullOrWhiteSpace(prompt) ? null : prompt);
        }
        finally
        {
            Sys.Stop(actor);
            File.Delete(audioPath);
        }
    }

    private sealed class TranscriptionResponseHandler : HttpMessageHandler
    {
        private string _requestBody = string.Empty;

        public int RequestCount { get; private set; }
        public HttpMethod? RequestMethod { get; private set; }
        public string? RequestPath { get; private set; }
        public string? RequestContentType { get; private set; }

        public string? GetField(string name)
        {
            var match = Regex.Match(_requestBody, "name=\"?" + Regex.Escape(name) + "\"?\\r\\n\\r\\n([^\\r\\n]+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestMethod = request.Method;
            RequestPath = request.RequestUri?.AbsolutePath;
            RequestContentType = request.Content?.Headers.ContentType?.MediaType;
            _requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"text\":\"Проверка совместимости.\",\"usage\":null}", Encoding.UTF8, "application/json")
            };
        }
    }
}
