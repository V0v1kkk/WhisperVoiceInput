namespace WhisperVoiceInput.Abstractions;

public interface IPipelineController
{
    void Reprocess();
    void CancelPipeline();
}
