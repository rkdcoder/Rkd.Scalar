using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Rkd.Scalar.OpenApi;
using Rkd.Scalar.Security.Contracts;
using Rkd.Scalar.Security.Jwt;

namespace Rkd.Scalar.Features
{
    /// <summary>
    /// JWT Bearer authentication feature (validation-only mode).
    /// Does not require credential model or validator.
    /// </summary>
    internal class BearerAuthFeature : IScalarFeature
    {
        private readonly JwtOptions _options;

        public BearerAuthFeature(JwtOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public virtual void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            var keys = JwtKeyMaterial.Create(_options);

            services.AddSingleton(_options);
            services.AddSingleton(keys);
            services.TryAddSingleton<IJwtTokenService>(sp =>
                new JwtTokenService(_options, sp.GetService<IJwtSigner>(), keys));

            services.AddAuthentication()
                .AddJwtBearer(jwt =>
                {
                    if (!string.IsNullOrWhiteSpace(_options.Authority))
                        jwt.Authority = _options.Authority;

                    if (!string.IsNullOrWhiteSpace(_options.MetadataAddress))
                        jwt.MetadataAddress = _options.MetadataAddress;

                    jwt.RequireHttpsMetadata = _options.RequireHttpsMetadata;

                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKeys = keys.ValidationKeys.Count > 0 ? keys.ValidationKeys : null,

                        ValidateIssuer = true,
                        ValidIssuer = string.IsNullOrEmpty(_options.Issuer) ? null : _options.Issuer,

                        ValidateAudience = true,
                        ValidAudience = string.IsNullOrEmpty(_options.Audience) ? null : _options.Audience,

                        ValidAlgorithms = string.IsNullOrWhiteSpace(_options.Algorithm)
                            ? null
                            : new[] { _options.Algorithm },

                        ValidateLifetime = true,
                        ClockSkew = _options.ClockSkew
                    };
                });

            services.ConfigureAll<OpenApiOptions>(options =>
            {
                options.AddDocumentTransformer<BearerSecurityTransformer>();
            });
        }

        public void ConfigureApp(WebApplication app)
        {
        }
    }

    /// <summary>
    /// JWT Bearer authentication feature with credential validation support
    /// (required by WithDefaultJwtLogin).
    /// </summary>
    internal sealed class BearerAuthFeature<TCredential, TValidator>
        : BearerAuthFeature, IBearerAuthFeature
        where TCredential : class
        where TValidator : class, ICredentialValidator<TCredential>
    {
        public Type CredentialType => typeof(TCredential);

        public BearerAuthFeature(JwtOptions options)
            : base(options)
        {
        }

        public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            base.ConfigureServices(services, configuration);
            services.AddScoped<ICredentialValidator<TCredential>, TValidator>();
        }
    }
}
