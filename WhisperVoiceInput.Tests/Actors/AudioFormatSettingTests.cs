using System;
using System.IO;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using NUnit.Framework;
using WhisperVoiceInput.Actors;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Tests.TestBase;
using WhisperVoiceInput.Tests.TestDoubles;

namespace WhisperVoiceInput.Tests.Actors;

/// <summary>
/// Tests for audio format setting (MP3 vs WAV) functionality
/// </summary>
[TestFixture]
public class AudioFormatSettingTests : AkkaTestBase
{
    private TestProbe _observerProbe = null!;
    private MockClipboardService _mockClipboardService = null!;
    
    [SetUp]
    public override void Setup()
    {
        base.Setup();
        _observerProbe = CreateTestProbe();
        _mockClipboardService = new MockClipboardService();
    }

    [Test]
    public void Should_Use_MP3_Format_When_UseWavFormat_Is_False()
    {
        // Arrange
        var settings = TestSettings with { UseWavFormat = false };
        var mockPropsFactory = new MockActorPropsFactory(
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TestScheduler);

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory,
                    _mockClipboardService,
                    Logger,
                    settings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-mp3"
        );

        // Act - Start recording
        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);

        // Stop recording to get the file path
        orchestrator.Tell(new ToggleCommand());
        
        // Advance time for recording to complete
        TestScheduler.Advance(TimeSpan.FromMilliseconds(500));
        
        // Wait for transcribing state
        AwaitAssert(() => orchestrator.StateName.Should().Be(AppState.Transcribing), TimeSpan.FromSeconds(2));

        // Assert - Check that the file path in StateData has .mp3 extension
        var stateData = orchestrator.StateData;
        stateData.LastAudioFilePath.Should().NotBeNullOrEmpty();
        Path.GetExtension(stateData.LastAudioFilePath).Should().Be(".mp3");
    }

    [Test]
    public void Should_Use_WAV_Format_When_UseWavFormat_Is_True()
    {
        // Arrange
        var settings = TestSettings with { UseWavFormat = true };
        var mockPropsFactory = new MockActorPropsFactory(
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TestScheduler);

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory,
                    _mockClipboardService,
                    Logger,
                    settings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-wav"
        );

        // Act - Start recording
        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);

        // Stop recording to get the file path
        orchestrator.Tell(new ToggleCommand());
        
        // Advance time for recording to complete
        TestScheduler.Advance(TimeSpan.FromMilliseconds(500));
        
        // Wait for transcribing state
        AwaitAssert(() => orchestrator.StateName.Should().Be(AppState.Transcribing), TimeSpan.FromSeconds(2));

        // Assert - Check that the file path in StateData has .wav extension
        var stateData = orchestrator.StateData;
        stateData.LastAudioFilePath.Should().NotBeNullOrEmpty();
        Path.GetExtension(stateData.LastAudioFilePath).Should().Be(".wav");
    }

    [Test]
    public void Should_Default_To_MP3_When_UseWavFormat_Not_Set()
    {
        // Arrange - Use default TestSettings (UseWavFormat defaults to false in AppSettings)
        var mockPropsFactory = new MockActorPropsFactory(
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TestScheduler);

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory,
                    _mockClipboardService,
                    Logger,
                    TestSettings, // Default settings
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-default"
        );

        // Act - Start recording
        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);

        // Stop recording to get the file path
        orchestrator.Tell(new ToggleCommand());
        
        // Advance time for recording to complete
        TestScheduler.Advance(TimeSpan.FromMilliseconds(500));
        
        // Wait for transcribing state
        AwaitAssert(() => orchestrator.StateName.Should().Be(AppState.Transcribing), TimeSpan.FromSeconds(2));

        // Assert - Should default to MP3
        var stateData = orchestrator.StateData;
        stateData.LastAudioFilePath.Should().NotBeNullOrEmpty();
        Path.GetExtension(stateData.LastAudioFilePath).Should().Be(".mp3");
    }

    [Test]
    public void Should_Persist_Format_Choice_Across_Multiple_Recordings()
    {
        // Arrange
        var settings = TestSettings with { UseWavFormat = true };
        var mockPropsFactory = new MockActorPropsFactory(
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TestScheduler);

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory,
                    _mockClipboardService,
                    Logger,
                    settings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-persist"
        );

        // First recording
        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new ToggleCommand());
        
        // Advance through all stages: recording -> transcribing -> saving -> idle
        TestScheduler.Advance(TimeSpan.FromMilliseconds(500)); // Recording
        AwaitAssert(() => orchestrator.StateName.Should().Be(AppState.Transcribing), TimeSpan.FromSeconds(2));
        
        TestScheduler.Advance(TimeSpan.FromMilliseconds(500)); // Transcribing
        AwaitAssert(() => orchestrator.StateName.Should().Be(AppState.Saving), TimeSpan.FromSeconds(2));
        
        TestScheduler.Advance(TimeSpan.FromMilliseconds(500)); // Saving
        AwaitAssert(() => orchestrator.StateName.Should().Be(AppState.Idle), TimeSpan.FromSeconds(2));
        
        var firstPath = orchestrator.StateData.LastAudioFilePath;
        Path.GetExtension(firstPath).Should().Be(".wav");

        // Second recording - should still use WAV
        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);
        orchestrator.Tell(new ToggleCommand());
        TestScheduler.Advance(TimeSpan.FromMilliseconds(500));
        AwaitAssert(() => orchestrator.StateName.Should().Be(AppState.Transcribing), TimeSpan.FromSeconds(2));
        
        var secondPath = orchestrator.StateData.LastAudioFilePath;
        Path.GetExtension(secondPath).Should().Be(".wav");
        secondPath.Should().NotBe(firstPath, "each recording should have a unique filename");
    }

    [Test]
    public void Should_Create_Unique_Filenames_For_Different_Formats()
    {
        // This test ensures that changing the format setting results in different file extensions
        // when starting new recordings (simulating settings change between recordings)
        
        // Arrange - First with MP3
        var mp3Settings = TestSettings with { UseWavFormat = false };
        var mockPropsFactory1 = new MockActorPropsFactory(
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TestScheduler);

        var orchestrator1 = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory1,
                    _mockClipboardService,
                    Logger,
                    mp3Settings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-mp3-unique"
        );

        orchestrator1.Tell(new ToggleCommand());
        orchestrator1.Tell(new ToggleCommand());
        TestScheduler.Advance(TimeSpan.FromMilliseconds(500));
        AwaitAssert(() => orchestrator1.StateName.Should().Be(AppState.Transcribing), TimeSpan.FromSeconds(2));
        
        var mp3Path = orchestrator1.StateData.LastAudioFilePath;

        // Arrange - Second with WAV (simulating settings change)
        var wavSettings = TestSettings with { UseWavFormat = true };
        var mockPropsFactory2 = new MockActorPropsFactory(
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TimeSpan.FromMilliseconds(100), 
            TestScheduler);

        var orchestrator2 = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory2,
                    _mockClipboardService,
                    Logger,
                    wavSettings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-wav-unique"
        );

        orchestrator2.Tell(new ToggleCommand());
        orchestrator2.Tell(new ToggleCommand());
        TestScheduler.Advance(TimeSpan.FromMilliseconds(500));
        AwaitAssert(() => orchestrator2.StateName.Should().Be(AppState.Transcribing), TimeSpan.FromSeconds(2));
        
        var wavPath = orchestrator2.StateData.LastAudioFilePath;

        // Assert - Different extensions
        Path.GetExtension(mp3Path).Should().Be(".mp3");
        Path.GetExtension(wavPath).Should().Be(".wav");
        Path.GetFileNameWithoutExtension(mp3Path).Should().NotBe(Path.GetFileNameWithoutExtension(wavPath), 
            "different recording sessions should have unique base filenames");
    }
}

