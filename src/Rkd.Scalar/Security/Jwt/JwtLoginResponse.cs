using System.Text.Json.Serialization;

namespace Rkd.Scalar.Security.Jwt
{
    /// <summary>
    /// OAuth2-style token response returned by the default login endpoint.
    /// Also handy as the response of your own login endpoint.
    /// </summary>
    public sealed class JwtLoginResponse
    {
        /// <summary>The issued JWT.</summary>
        [JsonPropertyName("access_token")]
        public required string AccessToken { get; init; }

        /// <summary>Token type. Always <c>Bearer</c>.</summary>
        [JsonPropertyName("token_type")]
        public string TokenType { get; init; } = "Bearer";

        /// <summary>Token lifetime in seconds, counted from the moment the response was created.</summary>
        [JsonPropertyName("expires_in")]
        public long ExpiresIn { get; init; }

        /// <summary>UTC instant when the token expires.</summary>
        [JsonPropertyName("expires_at")]
        public DateTime ExpiresAt { get; init; }

        /// <summary>
        /// Creates a response from a <see cref="JwtTokenResult"/>.
        /// </summary>
        public static JwtLoginResponse FromResult(JwtTokenResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            var expiresIn = (long)Math.Max(0, Math.Round((result.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds));

            return new JwtLoginResponse
            {
                AccessToken = result.Token,
                ExpiresIn = expiresIn,
                ExpiresAt = result.ExpiresAtUtc
            };
        }
    }
}
