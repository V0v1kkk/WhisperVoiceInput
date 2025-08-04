using System;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.Abstractions;

public interface ISettingsService
{
    IObservable<AppSettings> Settings { get; }
    AppSettings CurrentSettings { get; }
}