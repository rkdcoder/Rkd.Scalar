namespace Rkd.Scalar.Security.Jwt
{
    public sealed class JwtTokenResult
    {
        public required string Token { get; init; }
        public required DateTime ExpiresAtUtc { get; init; }
    }
}
