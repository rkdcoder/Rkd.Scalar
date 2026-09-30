using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Rkd.Scalar.Security.Jwt
{
    /// <summary>
    /// Default <see cref="IJwtTokenService"/>. Signs tokens with the key configured in <see cref="JwtOptions"/>
    /// or delegates the signature to an <see cref="IJwtSigner"/>.
    /// </summary>
    internal sealed class JwtTokenService : IJwtTokenService
    {
        private static readonly JsonWebTokenHandler Handler = new();

        private readonly JwtOptions _options;

        private readonly IJwtSigner? _signer;

        private readonly JwtKeyMaterial _keys;

        private readonly TimeProvider _time;

        public JwtTokenService(JwtOptions options, JwtKeyMaterial keys, IJwtSigner? signer = null, TimeProvider? time = null)
        {
            _options = options;
            _keys = keys;
            _signer = signer;
            _time = time ?? TimeProvider.System;
        }

        public async Task<JwtToken> CreateTokenAsync(
            ClaimsIdentity identity,
            IEnumerable<Claim>? additionalClaims = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(identity);

            var claims = identity.Claims.Concat(additionalClaims ?? []).ToList();

            // Whole seconds, like the numeric dates written to the token.
            var issuedAt = DateTimeOffset.FromUnixTimeSeconds(_time.GetUtcNow().ToUnixTimeSeconds());
            var expiresAt = issuedAt.Add(_options.Expiration);
            var tokenId = claims.FirstOrDefault(c => c.Type == "jti")?.Value ?? Guid.NewGuid().ToString("N");

            var payload = BuildPayload(claims, tokenId, issuedAt, expiresAt);

            string accessToken;

            if (_signer is not null)
            {
                accessToken = await SignWithSignerAsync(payload, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var key = _keys.SigningKey ?? throw new InvalidOperationException(
                    "No JWT signing key is configured. Token issuing requires Secret, " +
                    "PrivateKeyPem/PrivateKeyPath, SigningKey or a registered IJwtSigner.");

                accessToken = Handler.CreateToken(payload, new SigningCredentials(key, _keys.SigningAlgorithm));
            }

            return new JwtToken(accessToken, tokenId, issuedAt, expiresAt);
        }

        private string BuildPayload(
            IReadOnlyList<Claim> claims,
            string tokenId,
            DateTimeOffset issuedAt,
            DateTimeOffset expiresAt)
        {
            using var stream = new MemoryStream();

            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();

                if (!string.IsNullOrEmpty(_options.Issuer))
                    writer.WriteString("iss", _options.Issuer);

                var audiences = _options.AllAudiences;

                if (audiences.Count == 1)
                {
                    writer.WriteString("aud", audiences[0]);
                }
                else if (audiences.Count > 1)
                {
                    writer.WriteStartArray("aud");
                    foreach (var audience in audiences)
                        writer.WriteStringValue(audience);
                    writer.WriteEndArray();
                }

                writer.WriteNumber("iat", issuedAt.ToUnixTimeSeconds());
                writer.WriteNumber("nbf", issuedAt.ToUnixTimeSeconds());
                writer.WriteNumber("exp", expiresAt.ToUnixTimeSeconds());
                writer.WriteString("jti", tokenId);

                var groups = claims
                    .Select(c => (Name: ClaimName(c.Type), Claim: c))
                    .Where(c => c.Name != "jti" && !JwtClaimNames.Reserved.Contains(c.Name))
                    .GroupBy(c => c.Name, StringComparer.Ordinal);

                foreach (var group in groups)
                {
                    writer.WritePropertyName(group.Key);

                    var values = group.Select(c => c.Claim).ToList();

                    if (values.Count == 1)
                    {
                        WriteValue(writer, values[0]);
                    }
                    else
                    {
                        writer.WriteStartArray();
                        values.ForEach(v => WriteValue(writer, v));
                        writer.WriteEndArray();
                    }
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private string ClaimName(string type) =>
            _options.UseStandardClaimNames && JwtClaimNames.Outbound.TryGetValue(type, out var name)
                ? name
                : type;

        private static void WriteValue(Utf8JsonWriter writer, Claim claim)
        {
            switch (claim.ValueType)
            {
                case ClaimValueTypes.Boolean when bool.TryParse(claim.Value, out var boolean):
                    writer.WriteBooleanValue(boolean);
                    break;

                case ClaimValueTypes.Integer or ClaimValueTypes.Integer32 or ClaimValueTypes.Integer64
                    when long.TryParse(claim.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer):
                    writer.WriteNumberValue(integer);
                    break;

                case ClaimValueTypes.Double
                    when double.TryParse(claim.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number):
                    writer.WriteNumberValue(number);
                    break;

                default:
                    writer.WriteStringValue(claim.Value);
                    break;
            }
        }

        private async Task<string> SignWithSignerAsync(string payload, CancellationToken cancellationToken)
        {
            var signer = _signer!;

            if (string.IsNullOrWhiteSpace(signer.Algorithm))
                throw new InvalidOperationException("IJwtSigner.Algorithm must be provided.");

            var header = new Dictionary<string, string>
            {
                ["alg"] = signer.Algorithm,
                ["typ"] = "JWT"
            };

            if (!string.IsNullOrWhiteSpace(signer.KeyId))
                header["kid"] = signer.KeyId;

            var signingInput =
                Base64UrlEncoder.Encode(JsonSerializer.Serialize(header)) + "." +
                Base64UrlEncoder.Encode(payload);

            var signature = await signer
                .SignAsync(Encoding.ASCII.GetBytes(signingInput), cancellationToken)
                .ConfigureAwait(false);

            if (signature is null || signature.Length == 0)
                throw new InvalidOperationException("IJwtSigner returned an empty signature.");

            return signingInput + "." + Base64UrlEncoder.Encode(signature);
        }
    }
}
