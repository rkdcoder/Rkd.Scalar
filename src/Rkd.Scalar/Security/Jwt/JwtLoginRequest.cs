namespace Rkd.Scalar.Security.Jwt
{
    public sealed class JwtLoginRequest
    {
        public required string Username { get; init; }

        public required string Password { get; init; }
    }
}
