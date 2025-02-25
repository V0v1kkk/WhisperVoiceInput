using System;
using ReactiveUI;

namespace WhisperVoiceInput.ViewModels;

public class ViewModelBase : ReactiveObject, IDisposable
{
    public virtual void Dispose()
    {
        // Base implementation does nothing
        GC.SuppressFinalize(this);
    }
}