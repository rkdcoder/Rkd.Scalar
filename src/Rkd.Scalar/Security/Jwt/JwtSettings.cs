namespace Rkd.Scalar.Security.Jwt
{
    /// <summary>
    /// Configuration-friendly representation of <see cref="JwtOptions"/>,
    /// designed to be bound directly from appsettings.json.
    /// </summary>
    public sealed class JwtSettings
    {
        /// <inheritdoc cref="JwtOptions.Secret"/>
        public string Secret { get; set; } = string.Empty;

        /// <inheritdoc cref="JwtOptions.Issuer"/>
        public string Issuer { get; set; } = string.Empty;

        /// <inheritdoc cref="JwtOptions.Audience"/>
        public string Audience { get; set; } = string.Empty;

        /// <summary>
        /// Token lifetime in hours.
        /// </summary>
        public int Expiration { get; set; }

        /// <summary>
        /// Token lifetime in minutes. When greater than zero, takes precedence over <see cref="Expiration"/>.
        /// </summary>
        public int ExpirationMinutes { get; set; }

        /// <inheritdoc cref="JwtOptions.ValidateNotBefore"/>
        public bool ValidateNotBefore { get; set; }

        /// <inheritdoc cref="JwtOptions.Algorithm"/>
        public string? Algorithm { get; set; }

        /// <inheritdoc cref="JwtOptions.PrivateKeyPem"/>
        public string? PrivateKeyPem { get; set; }

        /// <inheritdoc cref="JwtOptions.PrivateKeyPath"/>
        public string? PrivateKeyPath { get; set; }

        /// <inheritdoc cref="JwtOptions.PublicKeyPem"/>
        public string? PublicKeyPem { get; set; }

        /// <inheritdoc cref="JwtOptions.PublicKeyPath"/>
        public string? PublicKeyPath { get; set; }

        /// <inheritdoc cref="JwtOptions.KeyId"/>
        public string? KeyId { get; set; }

        /// <inheritdoc cref="JwtOptions.Authority"/>
        public string? Authority { get; set; }

        /// <inheritdoc cref="JwtOptions.MetadataAddress"/>
        public string? MetadataAddress { get; set; }

        /// <inheritdoc cref="JwtOptions.RequireHttpsMetadata"/>
        public bool RequireHttpsMetadata { get; set; } = true;

        /// <summary>
        /// Clock skew tolerated when validating lifetime, in seconds. Defaults to zero.
        /// </summary>
        public int ClockSkewSeconds { get; set; }

        /// <summary>
        /// Converts the settings into <see cref="JwtOptions"/>.
        /// </summary>
        public JwtOptions ToJwtOptions() => new()
        {
            Secret = Secret,
            Issuer = Issuer,
            Audience = Audience,
            Expiration = ExpirationMinutes > 0
                ? TimeSpan.FromMinutes(ExpirationMinutes)
                : TimeSpan.FromHours(Expiration),
            ValidateNotBefore = ValidateNotBefore,
            Algorithm = Algorithm,
            PrivateKeyPem = PrivateKeyPem,
            PrivateKeyPath = PrivateKeyPath,
            PublicKeyPem = PublicKeyPem,
            PublicKeyPath = PublicKeyPath,
            KeyId = KeyId,
            Authority = Authority,
            MetadataAddress = MetadataAddress,
            RequireHttpsMetadata = RequireHttpsMetadata,
            ClockSkew = TimeSpan.FromSeconds(ClockSkewSeconds)
        };
    }
}
