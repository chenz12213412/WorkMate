using WorkMate.Models;

namespace WorkMate.Services;

public interface ISpeechEngine : IDisposable
{
    string BackendName { get; }

    IReadOnlyList<SpeechVoiceInfo> GetAvailableVoices();

    Task SpeakAsync(
        string text,
        string? voiceName,
        int volume,
        int rate,
        CancellationToken cancellationToken);

    void Stop();
}
