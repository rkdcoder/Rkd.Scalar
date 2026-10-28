using System.Text.Json.Serialization;

namespace Rkd.Scalar
{
    /// <summary>
    /// OAuth2-style token response returned by the login endpoint.
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

        /// <summary>When the token expires.</summary>
        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; init; }

        /// <summary>
        /// Creates a response from a <see cref="JwtToken"/>.
        /// </summary>
        public static JwtLoginResponse FromToken(JwtToken token)
        {
            ArgumentNullException.ThrowIfNull(token);

            var expiresIn = (long)Math.Max(0, Math.Round((token.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds));

            return new JwtLoginResponse
            {
                AccessToken = token.AccessToken,
                ExpiresIn = expiresIn,
                ExpiresAt = token.ExpiresAt
            };
        }
    }
}
