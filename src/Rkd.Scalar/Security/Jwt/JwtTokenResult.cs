namespace Rkd.Scalar.Security.Jwt
{
    /// <summary>
    /// A generated JWT and its expiration.
    /// </summary>
    public sealed class JwtTokenResult
    {
        /// <summary>The serialized (compact) JWT.</summary>
        public required string Token { get; init; }

        /// <summary>UTC instant when the token expires.</summary>
        public required DateTime ExpiresAtUtc { get; init; }
    }
}
