using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Rkd.Scalar.OpenApi;
using Rkd.Scalar.Security.Jwt;
using System.Security.Claims;

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
                new JwtTokenService(_options, keys, sp.GetService<IJwtSigner>(), sp.GetService<TimeProvider>()));

            services.AddAuthentication()
                .AddJwtBearer(jwt =>
                {
                    if (!string.IsNullOrWhiteSpace(_options.Authority))
                        jwt.Authority = _options.Authority;

                    if (!string.IsNullOrWhiteSpace(_options.MetadataAddress))
                        jwt.MetadataAddress = _options.MetadataAddress;

                    jwt.RequireHttpsMetadata = _options.RequireHttpsMetadata;

                    jwt.TokenHandlers.Clear();
                    jwt.TokenHandlers.Add(CreateTokenHandler());

                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKeys = keys.ValidationKeys.Count > 0 ? keys.ValidationKeys : null,

                        ValidateIssuer = true,
                        ValidIssuer = string.IsNullOrEmpty(_options.Issuer) ? null : _options.Issuer,

                        ValidateAudience = true,
                        ValidAudience = _options.Audience,

                        ValidAlgorithms = string.IsNullOrWhiteSpace(_options.Algorithm)
                            ? null
                            : new[] { _options.Algorithm },

                        ValidateLifetime = true,
                        ClockSkew = _options.ClockSkew,

                        NameClaimType = ClaimTypes.Name,
                        RoleClaimType = ClaimTypes.Role
                    };
                });

            services.ConfigureAll<OpenApiOptions>(options =>
            {
                options.AddDocumentTransformer<BearerSecurityTransformer>();
            });
        }

        /// <summary>
        /// Maps the standard claim names (<c>sub</c>, <c>name</c>, <c>role</c>…) back to <see cref="ClaimTypes"/>,
        /// so the principal looks the same whether the token uses standard or .NET claim names.
        /// </summary>
        private static JsonWebTokenHandler CreateTokenHandler()
        {
            var handler = new JsonWebTokenHandler { MapInboundClaims = true };

            foreach (var (standard, dotnet) in JwtClaimNames.Inbound)
                handler.InboundClaimTypeMap[standard] = dotnet;

            return handler;
        }

        public void ConfigureApp(WebApplication app)
        {
        }
    }

    /// <summary>
    /// JWT Bearer authentication feature with credential validation support
    /// (required by WithJwtLoginEndpoint).
    /// </summary>
    internal sealed class BearerAuthFeature<TCredentials, TValidator>
        : BearerAuthFeature, IBearerAuthFeature
        where TCredentials : class
        where TValidator : class, ICredentialValidator<TCredentials>
    {
        public Type CredentialType => typeof(TCredentials);

        public BearerAuthFeature(JwtOptions options)
            : base(options)
        {
        }

        public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            base.ConfigureServices(services, configuration);
            services.AddScoped<ICredentialValidator<TCredentials>, TValidator>();
        }
    }
}
