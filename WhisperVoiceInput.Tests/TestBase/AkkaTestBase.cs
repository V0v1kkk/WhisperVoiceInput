using Akka.TestKit.NUnit;
using Akka.TestKit;
using Akka.Configuration;
using NUnit.Framework;
using Serilog;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.Tests.TestBase;

/// <summary>
/// Base class for all Akka.NET actor tests.
/// Provides common test setup and utilities.
/// </summary>
public abstract class AkkaTestBase : TestKit
{
    protected ILogger Logger { get; private set; } = null!;
    protected AppSettings TestSettings { get; private set; } = null!;
    protected RetryPolicySettings TestRetrySettings { get; private set; } = null!;
    protected TestScheduler TestScheduler { get; private set; } = null!;

    protected AkkaTestBase() : base(GetTestConfig())
    {
    }

    private static Config GetTestConfig()
    {
        return ConfigurationFactory.ParseString(@"
                akka {
                    loglevel = DEBUG
                    stdout-loglevel = DEBUG
                    
                    actor {
                        debug {
                            receive = on
                            autoreceive = on
                            lifecycle = on
                            fsm = on
                        }
                    }
                    
                    test {
                        single-expect-default = 3s
                        filter-leeway = 3s
                        default-timeout = 5s
                    }
                    
                    scheduler {
                        implementation = ""Akka.TestKit.TestScheduler, Akka.TestKit""
                    }
                }
            ");
    }

    [SetUp]
    public virtual void Setup()
    {
        // Configure test logger
        Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger();

        // Initialize TestScheduler
        TestScheduler = (TestScheduler)Sys.Scheduler;

        // Create test settings
        TestSettings = new AppSettings
        {
            ServerAddress = "http://localhost:9000/v1/audio/transcriptions",
            ApiKey = "test-key",
            Model = "whisper-1",
            Language = "en",
            OutputType = ResultOutputType.ClipboardAvaloniaApi,
            PostProcessingEnabled = false,
            PostProcessingApiUrl = "http://localhost:11434/v1/chat/completions",
            PostProcessingModelName = "llama2",
            PostProcessingApiKey = ""
        };

        // Create test retry settings
        TestRetrySettings = new RetryPolicySettings
        {
            MaxRetries = 2,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            MaxDelay = TimeSpan.FromSeconds(1),
            StrategyType = SupervisionStrategyType.OneForOne
        };
    }

    [TearDown]
    public virtual void TearDown()
    {
        // TestKit automatically shuts down the actor system
        Shutdown();
    }

    /// <summary>
    /// Creates a test settings object with post-processing enabled
    /// </summary>
    protected AppSettings CreateSettingsWithPostProcessing()
    {
        return TestSettings with { PostProcessingEnabled = true };
    }
}