using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Content.Shared.Corvax.CCCVars;
using Prometheus;
using Robust.Shared.Configuration;

namespace Content.Server.Corvax.TTS;

// ReSharper disable once InconsistentNaming
public sealed class TTSManager
{
    private static readonly Histogram RequestTimings = Metrics.CreateHistogram(
        "tts_req_timings",
        "Timings of TTS API requests",
        new HistogramConfiguration
        {
            LabelNames = ["type"],
            Buckets = Histogram.ExponentialBuckets(.1, 1.5, 10),
        });

    private static readonly Counter WantedCount = Metrics.CreateCounter(
        "tts_wanted_count",
        "Amount of wanted TTS audio.");

    private static readonly Counter ReusedCount = Metrics.CreateCounter(
        "tts_reused_count",
        "Amount of reused TTS audio from cache.");

    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private readonly HttpClient _httpClient = new();

    private ISawmill _sawmill = default!;
    private readonly Dictionary<string, byte[]> _cache = new();
    private readonly List<string> _cacheKeysSeq = new();
    private readonly Dictionary<string, Task<byte[]?>> _pending = new();
    private CancellationTokenSource _requestsCts = new();
    private int _cacheGeneration;
    private int _maxCachedCount = 200;
    private string _apiUrl = string.Empty;

    public void Initialize()
    {
        _sawmill = Logger.GetSawmill("tts");
        _cfg.OnValueChanged(CCCVars.TTSMaxCache, val =>
        {
            _maxCachedCount = val;
            ResetCache();
        }, true);
        _cfg.OnValueChanged(CCCVars.TTSApiUrl, v => _apiUrl = v, true);
        _cfg.OnValueChanged(CCCVars.TTSApiToken,
            v =>
            {
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", v);
            }, true);
    }

    /// <summary>
    /// Generates audio with passed text by API
    /// </summary>
    /// <param name="speaker">Identifier of speaker</param>
    /// <param name="text">SSML formatted text</param>
    /// <returns>OGG audio bytes or null if failed</returns>
    public async Task<byte[]?> ConvertTextToSpeech(string speaker, string text, string? effect = null)
    {
        WantedCount.Inc();
        effect = string.IsNullOrWhiteSpace(effect) ? null : effect.Trim();
        var cacheKey = GenerateCacheKey(speaker, text, effect);
        if (_cache.TryGetValue(cacheKey, out var data))
        {
            ReusedCount.Inc();
            _sawmill.Verbose($"Use cached sound for '{text}' speech by '{speaker}' speaker");
            return data;
        }

        // Coalesce identical requests, including the entire retry chain.
        if (_pending.TryGetValue(cacheKey, out var pending))
            return await pending;

        var request = RequestSpeech(speaker, text, effect, cacheKey, _cacheGeneration, _requestsCts.Token);
        _pending[cacheKey] = request;
        try
        {
            return await request;
        }
        finally
        {
            if (_pending.TryGetValue(cacheKey, out var current) && current == request)
                _pending.Remove(cacheKey);
        }
    }

    private async Task<byte[]?> RequestSpeech(string speaker, string text, string? effect, string cacheKey,
        int generation, CancellationToken cancellation)
    {
        var retries = Math.Clamp(_cfg.GetCVar(CCCVars.TTSRetryCount), 0, 100);
        var configuredDelay = _cfg.GetCVar(CCCVars.TTSRetryDelay);
        var delay = TimeSpan.FromSeconds(float.IsFinite(configuredDelay) ? Math.Clamp(configuredDelay, 0f, 60f) : 0.5f);
        for (var attempt = 0; attempt <= retries; attempt++)
        {
            if (attempt > 0)
            {
                try
                {
                    await Task.Delay(delay, cancellation);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }
            if (generation != _cacheGeneration)
                return null;

            var reqTime = DateTime.UtcNow;
            string failure;
            try
            {
                var uri = new UriBuilder(_apiUrl);
                var query = HttpUtility.ParseQueryString(uri.Query);
                query["speaker"] = speaker;
                query["text"] = text;
                query["ext"] = "ogg";
                if (effect == null)
                    query.Remove("effect");
                else
                    query["effect"] = effect;
                uri.Query = query.ToString();
                var timeout = Math.Max(1, _cfg.GetCVar(CCCVars.TTSApiTimeout));
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                cts.CancelAfter(TimeSpan.FromSeconds(timeout));
                using var response = await _httpClient.GetAsync(uri.Uri, cts.Token);
                if (!response.IsSuccessStatusCode)
                    failure = $"HTTP {(int) response.StatusCode}";
                else
                {
                    var soundData = await response.Content.ReadAsByteArrayAsync(cts.Token);
                    // Some generators return a JSON error with HTTP 200 instead of audio.
                    if (soundData.Length < 4 || soundData[0] != 'O' || soundData[1] != 'g' ||
                        soundData[2] != 'g' || soundData[3] != 'S')
                        failure = "response does not contain OGG audio";
                    else
                    {
                        if (generation != _cacheGeneration)
                            return null;
                        if (_maxCachedCount > 0)
                        {
                            _cache[cacheKey] = soundData;
                            _cacheKeysSeq.Add(cacheKey);
                            while (_cache.Count > _maxCachedCount)
                            {
                                _cache.Remove(_cacheKeysSeq.First());
                                _cacheKeysSeq.RemoveAt(0);
                            }
                        }
                        RequestTimings.WithLabels("Success").Observe((DateTime.UtcNow - reqTime).TotalSeconds);
                        return soundData;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellation.IsCancellationRequested)
                    return null;
                failure = "request timed out";
            }
            catch (Exception e)
            {
                failure = e.GetType().Name;
            }
            RequestTimings.WithLabels("Error").Observe((DateTime.UtcNow - reqTime).TotalSeconds);
            if (attempt == retries)
                _sawmill.Error($"TTS generation failed after {attempt + 1} attempts: {failure}");
            else
                _sawmill.Warning($"TTS attempt {attempt + 1} failed ({failure}); retrying in {delay.TotalSeconds}s");
        }
        return null;
    }

    public void ResetCache()
    {
        _cacheGeneration++;
        var previous = _requestsCts;
        _requestsCts = new CancellationTokenSource();
        previous.Cancel();
        previous.Dispose();
        _pending.Clear();
        _cache.Clear();
        _cacheKeysSeq.Clear();
    }

    private string GenerateCacheKey(string speaker, string text, string? effect)
    {
        var key = System.Text.Json.JsonSerializer.Serialize(new[] { speaker, text, effect });
        var keyData = Encoding.UTF8.GetBytes(key);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(keyData);
        return Convert.ToHexString(bytes);
    }
}
