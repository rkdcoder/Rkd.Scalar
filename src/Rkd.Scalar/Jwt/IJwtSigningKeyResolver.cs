using Microsoft.IdentityModel.Tokens;

namespace Rkd.Scalar
{
    /// <summary>
    /// Finds the keys that validate an incoming JWT at runtime — from a database, a key registry or another
    /// service — by its <c>kid</c>. Registered with <c>WithJwtSigningKeyResolver&lt;T&gt;()</c>; the results are
    /// cached and the resolver is called again when a token carries an unknown <c>kid</c> (key rotation).
    /// </summary>
    /// <example>
    /// <code>
    /// public sealed class DbKeyResolver(KeysRepository keys) : IJwtSigningKeyResolver
    /// {
    ///     public async Task&lt;IEnumerable&lt;JwtSigningKey&gt;&gt; ResolveAsync(JwtSigningKeyContext context, CancellationToken cancellationToken)
    ///     {
    ///         var key = await keys.FindAsync(context.KeyId, cancellationToken);
    ///         return key is null ? [] : [new JwtSigningKey(key.ToSecurityKey(), issuer: key.Issuer)];
    ///     }
    /// }
    /// </code>
    /// </example>
    public interface IJwtSigningKeyResolver
    {
        /// <summary>
        /// Returns the keys that may validate tokens with <see cref="JwtSigningKeyContext.KeyId"/>
        /// (an empty result rejects the token). Returning every active key is also fine: each one is cached by its own
        /// <see cref="SecurityKey.KeyId"/>.
        /// </summary>
        /// <param name="context">Unverified information read from the token header and payload.</param>
        /// <param name="cancellationToken">Token used to cancel the lookup.</param>
        Task<IEnumerable<JwtSigningKey>> ResolveAsync(JwtSigningKeyContext context, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Key returned by <see cref="IJwtSigningKeyResolver"/>, optionally bound to the only issuer allowed to use it.
    /// </summary>
    public sealed class JwtSigningKey
    {
        /// <summary>Creates a validation key.</summary>
        /// <param name="key">Public key (RSA, ECDSA, X509) or shared secret. Its <see cref="SecurityKey.KeyId"/> should match the token <c>kid</c>.</param>
        /// <param name="issuer">
        /// When set, only tokens whose <c>iss</c> is exactly this value are accepted with this key — a service cannot
        /// sign tokens on behalf of another one. Required when <see cref="JwtOptions.Issuer"/> is not configured.
        /// </param>
        public JwtSigningKey(SecurityKey key, string? issuer = null)
        {
            ArgumentNullException.ThrowIfNull(key);

            Key = key;
            Issuer = string.IsNullOrWhiteSpace(issuer) ? null : issuer;
        }

        /// <summary>The validation key.</summary>
        public SecurityKey Key { get; }

        /// <summary>The only issuer accepted with <see cref="Key"/>, or <see langword="null"/> for any configured issuer.</summary>
        public string? Issuer { get; }
    }

    /// <summary>
    /// Information read from the token being validated. <b>Not verified yet</b>: use it only to find the key.
    /// </summary>
    public sealed class JwtSigningKeyContext
    {
        internal JwtSigningKeyContext(string? keyId, string? issuer, string? algorithm)
        {
            KeyId = keyId;
            Issuer = issuer;
            Algorithm = algorithm;
        }

        /// <summary>The <c>kid</c> header, or <see langword="null"/> when the token has none.</summary>
        public string? KeyId { get; }

        /// <summary>The <c>iss</c> claim as written in the token (unverified).</summary>
        public string? Issuer { get; }

        /// <summary>The <c>alg</c> header (e.g. <c>ES256</c>).</summary>
        public string? Algorithm { get; }
    }

    /// <summary>
    /// Cache settings of <see cref="IJwtSigningKeyResolver"/>.
    /// </summary>
    public sealed class JwtSigningKeyResolverOptions
    {
        /// <summary>How long resolved keys are reused before the resolver is called again. Defaults to 10 minutes.</summary>
        public TimeSpan KeyCacheDuration { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// How long a <c>kid</c> the resolver did not find stays rejected without calling it again, protecting the
        /// key store from tokens with random <c>kid</c> values. Defaults to 30 seconds; <see cref="TimeSpan.Zero"/>
        /// looks it up on every request.
        /// </summary>
        public TimeSpan UnknownKeyCacheDuration { get; set; } = TimeSpan.FromSeconds(30);
    }
}
