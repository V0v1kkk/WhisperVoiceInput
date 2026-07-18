using System.Reactive.Linq;
using System.Reactive.Subjects;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Serilog;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Actors;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.ViewModels;

namespace WhisperVoiceInput.Tests.ViewModels;

[TestFixture]
public class ApplicationViewModelTests
{
    private Subject<StateUpdatedEvent> _stateSubject = null!;
    private Subject<ReprocessAvailableEvent> _reprocessSubject = null!;
    private Mock<IPipelineController> _pipelineControllerMock = null!;
    private Mock<IStateObservableFactory> _stateObservableFactoryMock = null!;
    private Mock<ISettingsService> _settingsServiceMock = null!;
    private Mock<IRecordingToggler> _recordingTogglerMock = null!;
    private Mock<IClipboardService> _clipboardServiceMock = null!;
    private Mock<IGlobalHotkeyService> _globalHotkeyServiceMock = null!;
    private Mock<ILogBufferService> _logBufferServiceMock = null!;
    private Mock<IClassicDesktopStyleApplicationLifetime> _lifetimeMock = null!;
    private MainWindowViewModel _mainWindowViewModel = null!;
    private ApplicationViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _stateSubject = new Subject<StateUpdatedEvent>();
        _reprocessSubject = new Subject<ReprocessAvailableEvent>();

        _pipelineControllerMock = new Mock<IPipelineController>();
        _stateObservableFactoryMock = new Mock<IStateObservableFactory>();
        _stateObservableFactoryMock
            .Setup(f => f.GetStateObservable())
            .Returns(_stateSubject);
        _stateObservableFactoryMock
            .Setup(f => f.GetReprocessAvailableObservable())
            .Returns(_reprocessSubject);

        _settingsServiceMock = new Mock<ISettingsService>();

        _recordingTogglerMock = new Mock<IRecordingToggler>();
        _clipboardServiceMock = new Mock<IClipboardService>();
        _globalHotkeyServiceMock = new Mock<IGlobalHotkeyService>();
        _logBufferServiceMock = new Mock<ILogBufferService>();
        _lifetimeMock = new Mock<IClassicDesktopStyleApplicationLifetime>();

        _mainWindowViewModel = new MainWindowViewModel();
        _mainWindowViewModel.KeepLastRecordingInput = false;

        _viewModel = CreateViewModel();
        DrainUiThread();
    }

    [TearDown]
    public void TearDown()
    {
        _viewModel?.Dispose();
        _mainWindowViewModel?.Dispose();
        _stateSubject?.Dispose();
        _reprocessSubject?.Dispose();
    }

    [AvaloniaTest]
    public void Should_Report_PipelineBusy_When_AppState_Is_Recording()
    {
        EmitState(AppState.Recording);

        _viewModel.IsPipelineBusy.Should().BeTrue();
    }

    [AvaloniaTest]
    public void Should_Report_Not_PipelineBusy_When_AppState_Is_Idle()
    {
        EmitState(AppState.Idle);

        _viewModel.IsPipelineBusy.Should().BeFalse();
    }

    [AvaloniaTest]
    public void Should_Allow_Cancel_When_Pipeline_Is_Busy()
    {
        EmitState(AppState.Recording);

        _viewModel.CancelCommand.CanExecute.FirstAsync().Wait().Should().BeTrue();
    }

    [AvaloniaTest]
    public void Should_Block_Cancel_When_AppState_Is_Idle()
    {
        EmitState(AppState.Idle);

        _viewModel.CancelCommand.CanExecute.FirstAsync().Wait().Should().BeFalse();
    }

    [AvaloniaTest]
    public void Should_Block_Reprocess_When_AppState_Is_Busy()
    {
        EmitState(AppState.Transcribing);

        _viewModel.ReprocessCommand.CanExecute.FirstAsync().Wait().Should().BeFalse();
    }

    [AvaloniaTest]
    public void Should_Allow_Reprocess_When_AppState_Is_Idle_And_Conditions_Met()
    {
        _mainWindowViewModel.KeepLastRecordingInput = true;
        EmitState(AppState.Idle);
        EmitReprocessAvailable(true);

        _viewModel.ReprocessCommand.CanExecute.FirstAsync().Wait().Should().BeTrue();
    }

    [AvaloniaTest]
    public void Should_Not_Send_CancelCommand_When_Not_Busy()
    {
        EmitState(AppState.Idle);

        _viewModel.CancelCommand.Execute().Subscribe();

        _pipelineControllerMock.Verify(c => c.CancelPipeline(), Times.Never);
    }

    [AvaloniaTest]
    public void Should_Send_CancelCommand_When_Busy()
    {
        EmitState(AppState.Recording);

        _viewModel.CancelCommand.Execute().Subscribe();

        _pipelineControllerMock.Verify(c => c.CancelPipeline(), Times.Once);
    }

    [AvaloniaTest]
    public void Should_Not_Send_ReprocessCommand_When_Conditions_Not_Met()
    {
        EmitState(AppState.Idle);

        _viewModel.ReprocessCommand.Execute().Subscribe();

        _pipelineControllerMock.Verify(c => c.Reprocess(), Times.Never);
    }

    [AvaloniaTest]
    public void Should_Send_ReprocessCommand_When_Conditions_Met()
    {
        _mainWindowViewModel.KeepLastRecordingInput = true;
        EmitState(AppState.Idle);
        EmitReprocessAvailable(true);

        _viewModel.ReprocessCommand.Execute().Subscribe();

        _pipelineControllerMock.Verify(c => c.Reprocess(), Times.Once);
    }

    private ApplicationViewModel CreateViewModel()
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Fatal()
            .CreateLogger();

        return new ApplicationViewModel(
            _lifetimeMock.Object,
            logger,
            _mainWindowViewModel,
            _recordingTogglerMock.Object,
            _pipelineControllerMock.Object,
            _stateObservableFactoryMock.Object,
            _clipboardServiceMock.Object,
            _globalHotkeyServiceMock.Object,
            _logBufferServiceMock.Object);
    }

    private void EmitState(AppState state)
    {
        _stateSubject.OnNext(new StateUpdatedEvent(state));
        DrainUiThread();
    }

    private void EmitReprocessAvailable(bool isAvailable)
    {
        _reprocessSubject.OnNext(new ReprocessAvailableEvent(isAvailable));
        DrainUiThread();
    }

    private static void DrainUiThread()
    {
        Dispatcher.UIThread.RunJobs();
    }
}
