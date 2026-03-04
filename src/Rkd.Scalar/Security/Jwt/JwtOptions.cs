namespace Rkd.Scalar.Security.Jwt
{
    public sealed class JwtOptions
    {
        public required string Secret { get; set; }
        public required string Issuer { get; set; }
        public required string Audience { get; set; }
        public required TimeSpan Expiration { get; set; }
        public required bool ValidateNotBefore { get; set; }
    }
}