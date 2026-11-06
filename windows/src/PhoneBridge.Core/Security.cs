using System.Security.Cryptography;
using System.Text;

namespace PhoneBridge.Core;

public static class Tokens
{
    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>Tokens are stored only as SHA-256 hashes: a leaked database cannot be replayed.</summary>
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}

/// <summary>Key/value store for local secrets. Windows Credential Manager in the Host, in-memory in tests.</summary>
public interface ISecretStore
{
    string? Get(string key);
    void Set(string key, string value);
    void Delete(string key);
}

public sealed class InMemorySecretStore : ISecretStore
{
    readonly Dictionary<string, string> _values = new();
    public string? Get(string key) { lock (_values) return _values.GetValueOrDefault(key); }
    public void Set(string key, string value) { lock (_values) _values[key] = value; }
    public void Delete(string key) { lock (_values) _values.Remove(key); }
}

/// <summary>Sliding-window counter per key (IP address). Thread-safe.</summary>
public sealed class RateLimiter(int maxHits, TimeSpan window, TimeProvider? time = null)
{
    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly Dictionary<string, Queue<DateTimeOffset>> _hits = new();

    public bool IsLimited(string key)
    {
        lock (_hits) return Count(key) >= maxHits;
    }

    public void Hit(string key)
    {
        lock (_hits)
        {
            if (_hits.Count > 10_000) Prune();
            if (!_hits.TryGetValue(key, out var q)) _hits[key] = q = new Queue<DateTimeOffset>();
            q.Enqueue(_time.GetUtcNow());
        }
    }

    int Count(string key)
    {
        if (!_hits.TryGetValue(key, out var q)) return 0;
        var cutoff = _time.GetUtcNow() - window;
        while (q.Count > 0 && q.Peek() <= cutoff) q.Dequeue();
        return q.Count;
    }

    void Prune()
    {
        foreach (var key in _hits.Keys.ToList())
            if (Count(key) == 0) _hits.Remove(key);
    }
}

public enum PairOutcome { Ok, Invalid, Expired }

public sealed record PairingOffer(string Secret, DateTimeOffset ExpiresAt);

/// <summary>One active single-use pairing secret (32 bytes, 5 minutes). A new offer replaces the previous one.</summary>
public sealed class PairingManager(TimeProvider? time = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly object _lock = new();
    PairingOffer? _offer;
    bool _used;

    public PairingOffer CreateOffer()
    {
        lock (_lock)
        {
            _offer = new PairingOffer(Tokens.Base64Url(RandomNumberGenerator.GetBytes(32)), _time.GetUtcNow() + Lifetime);
            _used = false;
            return _offer;
        }
    }

    public void Cancel() { lock (_lock) _offer = null; }

    /// <summary>Expired = the secret was right but is too old or already used (HTTP 410).</summary>
    public PairOutcome TryRedeem(string? secret)
    {
        lock (_lock)
        {
            if (_offer is null || string.IsNullOrEmpty(secret) || !Tokens.FixedTimeEquals(secret, _offer.Secret))
                return PairOutcome.Invalid;
            if (_used || _time.GetUtcNow() >= _offer.ExpiresAt) return PairOutcome.Expired;
            _used = true;
            return PairOutcome.Ok;
        }
    }
}
