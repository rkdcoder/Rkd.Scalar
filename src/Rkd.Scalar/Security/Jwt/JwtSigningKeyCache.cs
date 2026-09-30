using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Rkd.Scalar.Security.Jwt
{
    /// <summary>
    /// Caches the keys returned by <see cref="IJwtSigningKeyResolver"/> by <c>kid</c>, calls it once per unknown
    /// <c>kid</c> (concurrent requests share the lookup) and remembers the issuer each key is bound to.
    /// </summary>
    internal sealed partial class JwtSigningKeyCache
    {
        private const int MaxEntries = 10_000;

        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<string, Lazy<Task<Entry>>> _pending = new(StringComparer.Ordinal);

        private readonly ConditionalWeakTable<SecurityKey, string> _issuers = new();

        private readonly IServiceScopeFactory _scopeFactory;

        private readonly JwtSigningKeyResolverOptions _options;

        private readonly TimeProvider _time;

        private readonly ILogger<JwtSigningKeyCache> _logger;

        public JwtSigningKeyCache(
            IServiceScopeFactory scopeFactory,
            JwtSigningKeyResolverOptions options,
            ILogger<JwtSigningKeyCache> logger,
            TimeProvider? time = null)
        {
            _scopeFactory = scopeFactory;
            _options = options;
            _logger = logger;
            _time = time ?? TimeProvider.System;
        }

        public async Task<IReadOnlyList<SecurityKey>> GetKeysAsync(JwtSigningKeyContext context, CancellationToken cancellationToken)
        {
            var cacheKey = context.KeyId ?? string.Empty;

            if (_entries.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > _time.GetUtcNow())
                return cached.Keys;

            // Concurrent requests with the same kid share one lookup, which is not tied to any of them.
            var lookup = _pending.GetOrAdd(cacheKey, _ => new Lazy<Task<Entry>>(() => ResolveAsync(context)));

            try
            {
                return (await lookup.Value.WaitAsync(cancellationToken)).Keys;
            }
            finally
            {
                _pending.TryRemove(new KeyValuePair<string, Lazy<Task<Entry>>>(cacheKey, lookup));
            }
        }

        /// <summary>
        /// Whether <paramref name="key"/> may validate a token issued by <paramref name="issuer"/>: keys bound to an
        /// issuer only accept that issuer; unbound resolved keys are accepted only when <paramref name="requireBoundIssuer"/>
        /// is <see langword="false"/>. Keys configured in <see cref="JwtOptions"/> are not affected.
        /// </summary>
        public bool IsAllowed(SecurityKey key, string? issuer, bool requireBoundIssuer, bool isResolved)
        {
            if (_issuers.TryGetValue(key, out var boundIssuer))
                return string.Equals(boundIssuer, issuer, StringComparison.Ordinal);

            return !isResolved || !requireBoundIssuer;
        }

        private async Task<Entry> ResolveAsync(JwtSigningKeyContext context)
        {
            IReadOnlyList<JwtSigningKey> resolved;

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var resolver = scope.ServiceProvider.GetRequiredService<IJwtSigningKeyResolver>();

                resolved = (await resolver.ResolveAsync(context, CancellationToken.None) ?? []).Where(k => k is not null).ToList();
            }
            catch (Exception exception)
            {
                // A failing key store must not turn every request into a 500: the token is rejected (401).
                LogResolverFailed(_logger, exception, context.KeyId);
                return new Entry([], _time.GetUtcNow());
            }

            var now = _time.GetUtcNow();

            if (_entries.Count > MaxEntries)
                Trim(now);

            foreach (var key in resolved)
            {
                if (key.Issuer is not null)
                    _issuers.AddOrUpdate(key.Key, key.Issuer);
            }

            // Every returned key is cached by its own kid, so resolvers may return all active keys at once.
            foreach (var group in resolved.Where(k => !string.IsNullOrEmpty(k.Key.KeyId)).GroupBy(k => k.Key.KeyId!, StringComparer.Ordinal))
                _entries[group.Key] = new Entry(group.Select(k => k.Key).ToList(), now + _options.KeyCacheDuration);

            var matching = resolved
                .Where(k => context.KeyId is null || string.IsNullOrEmpty(k.Key.KeyId) || k.Key.KeyId == context.KeyId)
                .Select(k => k.Key)
                .ToList();

            var entry = new Entry(matching, now + (matching.Count > 0 ? _options.KeyCacheDuration : _options.UnknownKeyCacheDuration));
            _entries[context.KeyId ?? string.Empty] = entry;

            if (matching.Count == 0)
                LogUnknownKey(_logger, context.KeyId, context.Issuer);

            return entry;
        }

        private void Trim(DateTimeOffset now)
        {
            foreach (var (key, entry) in _entries)
            {
                if (entry.ExpiresAt <= now || entry.Keys.Count == 0)
                    _entries.TryRemove(key, out _);
            }
        }

        private sealed record Entry(IReadOnlyList<SecurityKey> Keys, DateTimeOffset ExpiresAt);

        [LoggerMessage(EventId = 20, Level = LogLevel.Error, Message = "The JWT signing key resolver failed for kid '{KeyId}'; the token was rejected.")]
        private static partial void LogResolverFailed(ILogger logger, Exception exception, string? keyId);

        [LoggerMessage(EventId = 21, Level = LogLevel.Warning, Message = "No JWT signing key found for kid '{KeyId}' (issuer '{Issuer}'); the token was rejected.")]
        private static partial void LogUnknownKey(ILogger logger, string? keyId, string? issuer);
    }
}
