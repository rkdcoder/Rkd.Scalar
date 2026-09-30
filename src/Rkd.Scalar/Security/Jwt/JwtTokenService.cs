using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Rkd.Scalar.Security.Jwt
{
    /// <summary>
    /// Default <see cref="IJwtTokenService"/>. Signs tokens with the key configured in <see cref="JwtOptions"/>
    /// (HMAC secret, RSA/ECDSA key or <see cref="SecurityKey"/>) or delegates the signature to an <see cref="IJwtSigner"/>.
    /// </summary>
    public sealed class JwtTokenService : IJwtTokenService
    {
        private readonly JwtOptions _options;

        private readonly IJwtSigner? _signer;

        private readonly Lazy<JwtKeyMaterial> _keys;

        /// <summary>
        /// Creates a token service that signs with the key configured in <paramref name="options"/>.
        /// </summary>
        public JwtTokenService(JwtOptions options)
            : this(options, signer: null)
        {
        }

        /// <summary>
        /// Creates a token service that delegates the signature to <paramref name="signer"/> when provided.
        /// </summary>
        public JwtTokenService(JwtOptions options, IJwtSigner? signer)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _signer = signer;
            _keys = new Lazy<JwtKeyMaterial>(() => JwtKeyMaterial.Create(options));
        }

        internal JwtTokenService(JwtOptions options, IJwtSigner? signer, JwtKeyMaterial keys)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _signer = signer;
            _keys = new Lazy<JwtKeyMaterial>(keys);
        }

        /// <inheritdoc />
        /// <remarks>
        /// When an <see cref="IJwtSigner"/> is registered this method blocks on the asynchronous signature;
        /// prefer <see cref="GenerateTokenAsync"/>.
        /// </remarks>
        public JwtTokenResult GenerateToken(
            ClaimsIdentity identity,
            IEnumerable<Claim>? additionalClaims = null)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            if (_signer is not null)
                return GenerateWithSignerAsync(identity, additionalClaims, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

            var keys = _keys.Value;

            if (keys.SigningKey is null)
                throw new InvalidOperationException(
                    "No JWT signing key is configured. Token issuing requires JwtOptions.Secret, " +
                    "PrivateKeyPem/PrivateKeyPath, SigningKey or a registered IJwtSigner.");

            var now = DateTime.UtcNow;

            var signingCredentials = new SigningCredentials(
                keys.SigningKey,
                keys.SigningAlgorithm);

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: BuildClaims(identity, additionalClaims),
                notBefore: _options.ValidateNotBefore ? now : null,
                expires: now.Add(_options.Expiration),
                signingCredentials: signingCredentials
            );

            var handler = new JwtSecurityTokenHandler();

            var tokenString = handler.WriteToken(token);

            return new JwtTokenResult
            {
                Token = tokenString,
                ExpiresAtUtc = token.ValidTo
            };
        }

        /// <inheritdoc />
        public Task<JwtTokenResult> GenerateTokenAsync(
            ClaimsIdentity identity,
            IEnumerable<Claim>? additionalClaims = null,
            CancellationToken cancellationToken = default)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            if (_signer is not null)
                return GenerateWithSignerAsync(identity, additionalClaims, cancellationToken);

            return Task.FromResult(GenerateToken(identity, additionalClaims));
        }

        private async Task<JwtTokenResult> GenerateWithSignerAsync(
            ClaimsIdentity identity,
            IEnumerable<Claim>? additionalClaims,
            CancellationToken cancellationToken)
        {
            var signer = _signer!;

            if (string.IsNullOrWhiteSpace(signer.Algorithm))
                throw new InvalidOperationException("IJwtSigner.Algorithm must be provided.");

            var now = DateTime.UtcNow;

            var header = new JwtHeader
            {
                [JwtHeaderParameterNames.Alg] = signer.Algorithm,
                [JwtHeaderParameterNames.Typ] = JwtConstants.HeaderType
            };

            if (!string.IsNullOrWhiteSpace(signer.KeyId))
                header[JwtHeaderParameterNames.Kid] = signer.KeyId;

            var payload = new JwtPayload(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: BuildClaims(identity, additionalClaims),
                notBefore: _options.ValidateNotBefore ? now : null,
                expires: now.Add(_options.Expiration));

            var signingInput = header.Base64UrlEncode() + "." + payload.Base64UrlEncode();

            var signature = await signer
                .SignAsync(Encoding.ASCII.GetBytes(signingInput), cancellationToken)
                .ConfigureAwait(false);

            if (signature is null || signature.Length == 0)
                throw new InvalidOperationException("IJwtSigner returned an empty signature.");

            return new JwtTokenResult
            {
                Token = signingInput + "." + Base64UrlEncoder.Encode(signature),
                ExpiresAtUtc = payload.ValidTo
            };
        }

        private static List<Claim> BuildClaims(
            ClaimsIdentity identity,
            IEnumerable<Claim>? additionalClaims)
        {
            var claims = identity.Claims.ToList();

            if (additionalClaims != null)
                claims.AddRange(additionalClaims);

            return claims;
        }
    }
}
