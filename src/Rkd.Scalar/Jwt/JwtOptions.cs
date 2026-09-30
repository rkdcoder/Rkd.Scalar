using Microsoft.IdentityModel.Tokens;

namespace Rkd.Scalar
{
    /// <summary>
    /// Options used to issue and validate JWT tokens. Bindable from configuration
    /// (the <c>Jwt</c> section by default).
    /// </summary>
    /// <remarks>
    /// Configure exactly one key source:
    /// <list type="bullet">
    /// <item><see cref="Secret"/> — HMAC (HS256) shared secret, at least 32 characters.</item>
    /// <item><see cref="PrivateKeyPem"/> / <see cref="PrivateKeyPath"/> — RSA or ECDSA private key (RS256, ES256…).</item>
    /// <item><see cref="PublicKeyPem"/> / <see cref="PublicKeyPath"/> — RSA or ECDSA public key (validation only).</item>
    /// <item><see cref="SigningKey"/> — any <see cref="SecurityKey"/> (X509, RSA, ECDSA, JsonWebKey…).</item>
    /// <item><see cref="Authority"/> / <see cref="MetadataAddress"/> — OpenID Connect provider (validation only).</item>
    /// </list>
    /// When an <see cref="IJwtSigner"/> is registered, tokens are signed by it and the options above are
    /// only used for validation.
    /// </remarks>
    /// <example>
    /// <code>
    /// "Jwt": {
    ///   "Secret": "...",
    ///   "Issuer": "my-api",
    ///   "Audience": "my-clients",
    ///   "ExpirationInMinutes": 60
    /// }
    /// </code>
    /// </example>
    public sealed class JwtOptions
    {
        /// <summary>
        /// HMAC shared secret (minimum 32 characters). Leave empty when using an asymmetric key or an <see cref="Authority"/>.
        /// </summary>
        public string? Secret { get; set; }

        /// <summary>
        /// Token issuer (<c>iss</c>). Required unless <see cref="Authority"/> is used, in which case it
        /// defaults to the issuer of the discovery document.
        /// </summary>
        public string? Issuer { get; set; }

        /// <summary>
        /// Token audience (<c>aud</c>). Required.
        /// </summary>
        public string? Audience { get; set; }

        /// <summary>
        /// Lifetime of issued tokens, in minutes. Defaults to 60.
        /// </summary>
        public int ExpirationInMinutes { get; set; } = 60;

        /// <summary>
        /// Clock difference tolerated when validating <c>exp</c> and <c>nbf</c>, in seconds. Defaults to 30.
        /// </summary>
        public int ClockSkewInSeconds { get; set; } = 30;

        /// <summary>
        /// When <see langword="true"/> (default), claims are written with the standard JWT names
        /// (<c>sub</c>, <c>name</c>, <c>role</c>, <c>email</c>…) instead of the long .NET URIs, and mapped back to
        /// <c>ClaimTypes.*</c> on validation, so <c>User.Identity.Name</c>, <c>[Authorize(Roles = ...)]</c>
        /// and <c>ClaimTypes.NameIdentifier</c> keep working.
        /// </summary>
        public bool UseStandardClaimNames { get; set; } = true;

        /// <summary>
        /// Signing algorithm (for example <c>HS256</c>, <c>RS256</c>, <c>PS256</c>, <c>ES256</c>).
        /// When <see langword="null"/>, it is inferred from the key. When set, validation only accepts this algorithm.
        /// </summary>
        public string? Algorithm { get; set; }

        /// <summary>
        /// PEM encoded RSA or ECDSA private key (PKCS#1, PKCS#8 or SEC1). Used to sign and validate tokens.
        /// </summary>
        public string? PrivateKeyPem { get; set; }

        /// <summary>
        /// Path to a PEM file containing an RSA or ECDSA private key. Useful with mounted secrets (Kubernetes, Docker).
        /// </summary>
        public string? PrivateKeyPath { get; set; }

        /// <summary>
        /// PEM encoded RSA or ECDSA public key (SubjectPublicKeyInfo or PKCS#1). Used to validate tokens only.
        /// </summary>
        public string? PublicKeyPem { get; set; }

        /// <summary>
        /// Path to a PEM file containing an RSA or ECDSA public key. Used to validate tokens only.
        /// </summary>
        public string? PublicKeyPath { get; set; }

        /// <summary>
        /// Key identifier written to the token header (<c>kid</c>) and exposed by the JWKS endpoint.
        /// When <see langword="null"/>, asymmetric keys get their RFC 7638 thumbprint as identifier.
        /// </summary>
        public string? KeyId { get; set; }

        /// <summary>
        /// Programmatic signing key (for example an <c>X509SecurityKey</c>, <c>RsaSecurityKey</c> or <c>ECDsaSecurityKey</c>).
        /// Takes precedence over the PEM and secret options. Not bindable from configuration.
        /// </summary>
        public SecurityKey? SigningKey { get; set; }

        /// <summary>
        /// Additional keys accepted during validation (for example the previous key during a key rotation).
        /// Not bindable from configuration.
        /// </summary>
        public IList<SecurityKey> ValidationKeys { get; } = new List<SecurityKey>();

        /// <summary>
        /// OpenID Connect authority (for example <c>https://login.microsoftonline.com/{tenant}/v2.0</c> or a Keycloak realm).
        /// Signing keys and issuer are downloaded from its discovery document.
        /// </summary>
        public string? Authority { get; set; }

        /// <summary>
        /// Explicit OpenID Connect discovery document address. Defaults to <c>{Authority}/.well-known/openid-configuration</c>.
        /// </summary>
        public string? MetadataAddress { get; set; }

        /// <summary>
        /// Whether the metadata address must use HTTPS. Defaults to <see langword="true"/>.
        /// </summary>
        public bool RequireHttpsMetadata { get; set; } = true;

        internal TimeSpan Expiration => TimeSpan.FromMinutes(ExpirationInMinutes);

        internal TimeSpan ClockSkew => TimeSpan.FromSeconds(ClockSkewInSeconds);

        internal bool HasStaticKeys =>
            !string.IsNullOrEmpty(Secret) || !string.IsNullOrWhiteSpace(PrivateKeyPem) || !string.IsNullOrWhiteSpace(PrivateKeyPath) ||
            !string.IsNullOrWhiteSpace(PublicKeyPem) || !string.IsNullOrWhiteSpace(PublicKeyPath) ||
            SigningKey is not null || ValidationKeys.Count > 0;

        internal bool HasAuthority =>
            !string.IsNullOrWhiteSpace(Authority) || !string.IsNullOrWhiteSpace(MetadataAddress);

        /// <summary>
        /// Fails fast on settings that would make every token invalid.
        /// </summary>
        internal void Validate(string source, bool hasKeyResolver = false)
        {
            if (string.IsNullOrWhiteSpace(Audience))
                throw new InvalidOperationException($"{source}: 'Audience' is required.");

            // Without Issuer, only resolved keys bound to their issuer can validate tokens: static keys would accept any issuer.
            if (string.IsNullOrWhiteSpace(Issuer) && !HasAuthority && (!hasKeyResolver || HasStaticKeys))
                throw new InvalidOperationException(
                    $"{source}: 'Issuer' is required (or configure 'Authority', or bind each key to its issuer with WithJwtSigningKeyResolver<T>()).");

            if (ExpirationInMinutes <= 0)
                throw new InvalidOperationException($"{source}: 'ExpirationInMinutes' must be greater than zero.");

            if (ClockSkewInSeconds < 0)
                throw new InvalidOperationException($"{source}: 'ClockSkewInSeconds' cannot be negative.");
        }
    }
}
