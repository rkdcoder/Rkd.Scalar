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
        /// sign tokens on behalf of another one. Required when <c>JwtOptions.Issuer</c> is not configured.
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
        /// <summary>
        /// Creates the context. Rkd.Scalar builds it from each incoming token; create one yourself to unit test a resolver.
        /// </summary>
        /// <param name="keyId">The <c>kid</c> header.</param>
        /// <param name="issuer">The <c>iss</c> claim.</param>
        /// <param name="algorithm">The <c>alg</c> header.</param>
        public JwtSigningKeyContext(string? keyId, string? issuer, string? algorithm)
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
}
