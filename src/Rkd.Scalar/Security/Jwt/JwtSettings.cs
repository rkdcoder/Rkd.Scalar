namespace Rkd.Scalar.Security.Jwt
{
    /// <summary>
    /// Configuration-friendly representation of <see cref="JwtOptions"/>,
    /// designed to be bound directly from appsettings.json.
    /// </summary>
    public sealed class JwtSettings
    {
        public string Secret { get; set; } = string.Empty;

        public string Issuer { get; set; } = string.Empty;

        public string Audience { get; set; } = string.Empty;

        /// <summary>
        /// Token lifetime in hours.
        /// </summary>
        public int Expiration { get; set; }

        public bool ValidateNotBefore { get; set; }

        public JwtOptions ToJwtOptions() => new()
        {
            Secret = Secret,
            Issuer = Issuer,
            Audience = Audience,
            Expiration = TimeSpan.FromHours(Expiration),
            ValidateNotBefore = ValidateNotBefore
        };
    }
}