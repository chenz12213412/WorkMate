using System.Speech.Synthesis;
using WorkMate.Infrastructure;
using WorkMate.Models;

namespace WorkMate.Services;

public sealed class SapiSpeechEngine : ISpeechEngine
{
    private readonly object _syncRoot = new();
    private SpeechSynthesizer? _activeSynthesizer;
    private long _stopGeneration;
    private bool _disposed;

    public string BackendName => "System.Speech / SAPI";

    public IReadOnlyList<SpeechVoiceInfo> GetAvailableVoices()
    {
        if (_disposed)
        {
            return [];
        }

        try
        {
            using var synthesizer = new SpeechSynthesizer();
            return synthesizer.GetInstalledVoices()
                .Where(static voice => voice.Enabled)
                .Select(static voice => new SpeechVoiceInfo(
                    voice.VoiceInfo.Name,
                    voice.VoiceInfo.Culture.Name,
                    voice.VoiceInfo.Gender.ToString(),
                    voice.VoiceInfo.Description))
                .OrderByDescending(static voice =>
                    voice.Culture.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
                .ThenBy(static voice => voice.Culture)
                .ThenBy(static voice => voice.Name)
                .ToArray();
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
            return [];
        }
    }

    public async Task SpeakAsync(
        string text,
        string? voiceName,
        int volume,
        int rate,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var generation = Volatile.Read(ref _stopGeneration);

        var synthesizer = new SpeechSynthesizer
        {
            Volume = Math.Clamp(volume, 0, 100),
            Rate = Math.Clamp(rate, -10, 10)
        };
        SelectVoiceWithFallback(synthesizer, voiceName);

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<SpeakCompletedEventArgs>? handler = null;
        handler = (_, args) =>
        {
            if (args.Error is not null)
            {
                completion.TrySetException(args.Error);
            }
            else if (args.Cancelled)
            {
                completion.TrySetCanceled();
            }
            else
            {
                completion.TrySetResult();
            }
        };
        synthesizer.SpeakCompleted += handler;

        lock (_syncRoot)
        {
            if (_disposed)
            {
                synthesizer.Dispose();
                return;
            }

            _activeSynthesizer = synthesizer;
        }

        using var registration = cancellationToken.Register(Stop);
        try
        {
            lock (_syncRoot)
            {
                if (generation != Volatile.Read(ref _stopGeneration))
                {
                    return;
                }

                synthesizer.SpeakAsync(text);
            }
            await completion.Task;
        }
        finally
        {
            synthesizer.SpeakCompleted -= handler;
            lock (_syncRoot)
            {
                if (ReferenceEquals(_activeSynthesizer, synthesizer))
                {
                    _activeSynthesizer = null;
                }
            }

            synthesizer.Dispose();
        }
    }

    public void Stop()
    {
        Interlocked.Increment(ref _stopGeneration);
        lock (_syncRoot)
        {
            if (!_disposed)
            {
                _activeSynthesizer?.SpeakAsyncCancelAll();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
        lock (_syncRoot)
        {
            _activeSynthesizer?.Dispose();
            _activeSynthesizer = null;
        }
    }

    private static void SelectVoiceWithFallback(SpeechSynthesizer synthesizer, string? voiceName)
    {
        var voices = synthesizer.GetInstalledVoices()
            .Where(static voice => voice.Enabled)
            .ToArray();
        var selected = !string.IsNullOrWhiteSpace(voiceName)
            ? voices.FirstOrDefault(voice => string.Equals(
                voice.VoiceInfo.Name,
                voiceName,
                StringComparison.OrdinalIgnoreCase))
            : null;
        selected ??= voices.FirstOrDefault(static voice =>
            voice.VoiceInfo.Culture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase));
        selected ??= voices.FirstOrDefault(static voice =>
            voice.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals(
                "zh",
                StringComparison.OrdinalIgnoreCase));

        if (selected is not null)
        {
            synthesizer.SelectVoice(selected.VoiceInfo.Name);
        }
    }
}
