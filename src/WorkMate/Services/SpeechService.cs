using WorkMate.Infrastructure;
using WorkMate.Models;

namespace WorkMate.Services;

public sealed class SpeechService : IDisposable
{
    private readonly SemaphoreSlim _speechLock = new(1, 1);
    private readonly ISpeechEngine _engine;
    private bool _enabled = true;
    private int _volume = 80;
    private int _ratePreset;
    private string? _voiceName;
    private long _stopGeneration;
    private bool _disposed;

    public SpeechService()
        : this(new SapiSpeechEngine())
    {
    }

    internal SpeechService(ISpeechEngine engine)
    {
        _engine = engine;
    }

    public string BackendName => _engine.BackendName;

    public IReadOnlyList<SpeechVoiceInfo> GetAvailableVoices() => _engine.GetAvailableVoices();

    public void UpdateSettings(bool enabled, int volume, int rate, string? voiceName = null)
    {
        _enabled = enabled;
        _volume = Math.Clamp(volume, 0, 100);
        _ratePreset = Math.Clamp(rate, -1, 1);
        _voiceName = string.IsNullOrWhiteSpace(voiceName) ? null : voiceName.Trim();
        if (!enabled)
        {
            Stop();
        }
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(text) || _disposed)
        {
            return;
        }

        var generation = Volatile.Read(ref _stopGeneration);
        await _speechLock.WaitAsync(cancellationToken);
        try
        {
            if (!_enabled || _disposed || generation != Volatile.Read(ref _stopGeneration))
            {
                return;
            }

            var spokenText = SpeechTextNormalizer.Normalize(text);
            if (spokenText.Length == 0)
            {
                return;
            }

            try
            {
                await _engine.SpeakAsync(
                    spokenText,
                    _voiceName,
                    _volume,
                    MapRate(_ratePreset),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Stop() 抢占当前普通语音，不作为错误记录。
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                FileLogger.Write(exception);
            }
        }
        finally
        {
            _speechLock.Release();
        }
    }

    public void Stop()
    {
        Interlocked.Increment(ref _stopGeneration);
        if (!_disposed)
        {
            _engine.Stop();
        }
    }

    public Task TestAsync(CancellationToken cancellationToken)
    {
        return SpeakAsync(
            "你好，我是 WorkMate。工作的时候，也记得照顾好自己。",
            cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
        _engine.Dispose();
        _speechLock.Dispose();
    }

    private static int MapRate(int preset)
    {
        return preset switch
        {
            < 0 => -3,
            > 0 => 1,
            _ => -1
        };
    }
}
