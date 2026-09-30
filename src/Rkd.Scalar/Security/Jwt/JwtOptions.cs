using Microsoft.IdentityModel.Tokens;

namespace Rkd.Scalar.Security.Jwt
{
    /// <summary>
    /// Options used to issue and validate JWT tokens.
    /// </summary>
    /// <remarks>
    /// Configure exactly one key source:
    /// <list type="bullet">
    /// <item><see cref="Secret"/> — HMAC (HS256) shared secret, at least 32 characters.</item>
    /// <item><see cref="PrivateKeyPem"/> / <see cref="PrivateKeyPath"/> — RSA or ECDSA private key (RS256 / ES256…).</item>
    /// <item><see cref="PublicKeyPem"/> / <see cref="PublicKeyPath"/> — RSA or ECDSA public key (validation only).</item>
    /// <item><see cref="SigningKey"/> — any <see cref="SecurityKey"/> (X509, RSA, ECDSA, JsonWebKey…).</item>
    /// <item><see cref="Authority"/> / <see cref="MetadataAddress"/> — external identity provider (OIDC discovery / JWKS, validation only).</item>
    /// </list>
    /// When an <see cref="IJwtSigner"/> is registered (async/remote signing, e.g. a KMS or Key Vault),
    /// tokens are signed by it and the options above are only used for validation.
    /// </remarks>
    public sealed class JwtOptions
    {
        /// <summary>
        /// HMAC shared secret (minimum 32 characters). Leave empty when using an asymmetric key or an <see cref="Authority"/>.
        /// </summary>
        public string Secret { get; set; } = string.Empty;

        /// <summary>
        /// Token issuer (<c>iss</c>). When <see cref="Authority"/> is used and this is empty, the issuer comes from the discovery document.
        /// </summary>
        public string Issuer { get; set; } = string.Empty;

        /// <summary>
        /// Token audience (<c>aud</c>).
        /// </summary>
        public string Audience { get; set; } = string.Empty;

        /// <summary>
        /// Lifetime of issued tokens. Defaults to one hour.
        /// </summary>
        public TimeSpan Expiration { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// When <see langword="true"/>, issued tokens carry an <c>nbf</c> (not before) claim.
        /// </summary>
        public bool ValidateNotBefore { get; set; }

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
        /// Takes precedence over the PEM and secret options.
        /// </summary>
        public SecurityKey? SigningKey { get; set; }

        /// <summary>
        /// Additional keys accepted during validation (for example the previous key during a key rotation).
        /// </summary>
        public IList<SecurityKey> ValidationKeys { get; set; } = new List<SecurityKey>();

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

        /// <summary>
        /// Clock skew tolerated when validating lifetime. Defaults to <see cref="TimeSpan.Zero"/>.
        /// </summary>
        public TimeSpan ClockSkew { get; set; } = TimeSpan.Zero;
    }
}
