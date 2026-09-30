using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Rkd.Scalar.Errors;
using Rkd.Scalar.Infrastructure;
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

        private readonly ScalarFeatureRegistry _registry;

        public BearerAuthFeature(JwtOptions options, ScalarFeatureRegistry registry)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _registry = registry;
        }

        public virtual void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            var keys = JwtKeyMaterial.Create(_options, allowNoKeys: _registry.HasJwtSigningKeyResolver);

            services.AddSingleton(_options);
            services.AddSingleton(keys);
            services.TryAddSingleton<IJwtTokenService>(sp =>
                new JwtTokenService(_options, keys, sp.GetService<IJwtSigner>(), sp.GetService<TimeProvider>()));

            services.AddAuthentication().AddJwtBearer();

            // Runs when the options are first used, so every builder call (ConfigureJwtBearer,
            // WithJwtSigningKeyResolver) is taken into account whatever its order.
            services.AddOptions<JwtBearerOptions>(RkdScalarAuthenticationSchemes.Bearer)
                .Configure<IServiceProvider>((jwt, serviceProvider) =>
                {
                    if (!string.IsNullOrWhiteSpace(_options.Authority))
                        jwt.Authority = _options.Authority;

                    if (!string.IsNullOrWhiteSpace(_options.MetadataAddress))
                        jwt.MetadataAddress = _options.MetadataAddress;

                    jwt.RequireHttpsMetadata = _options.RequireHttpsMetadata;

                    // Without Issuer, resolved keys must be bound to an issuer (JwtSigningKey.Issuer).
                    var issuerFromKeys = string.IsNullOrEmpty(_options.Issuer) && !_options.HasAuthority;

                    jwt.TokenHandlers.Clear();
                    jwt.TokenHandlers.Add(new RkdJsonWebTokenHandler(
                        _registry.HasJwtSigningKeyResolver ? serviceProvider.GetRequiredService<JwtSigningKeyCache>() : null,
                        requireBoundIssuer: issuerFromKeys));

                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKeys = keys.ValidationKeys.Count > 0 ? keys.ValidationKeys : null,

                        ValidateIssuer = !issuerFromKeys,
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

                    foreach (var configure in _registry.JwtBearerConfigurations)
                        configure(jwt);

                    CaptureAuthenticationFailures(jwt);
                });

            services.ConfigureAll<OpenApiOptions>(options =>
            {
                options.AddDocumentTransformer<BearerSecurityTransformer>();
            });
        }

        /// <summary>
        /// Keeps a <see cref="ProblemException"/> passed to <c>context.Fail(...)</c> (e.g. in <c>OnTokenValidated</c>)
        /// so <c>WithProblemDetails()</c> writes its <c>code</c> and <c>detail</c> in the 401 response.
        /// Composes with your own <c>OnChallenge</c>; not applied when <c>EventsType</c> is used.
        /// </summary>
        private static void CaptureAuthenticationFailures(JwtBearerOptions jwt)
        {
            if (jwt.EventsType is not null)
                return;

            jwt.Events ??= new JwtBearerEvents();

            var onChallenge = jwt.Events.OnChallenge;

            jwt.Events.OnChallenge = context =>
            {
                AuthenticationFailures.Capture(context.HttpContext, context.AuthenticateFailure);
                return onChallenge(context);
            };
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

        public BearerAuthFeature(JwtOptions options, ScalarFeatureRegistry registry)
            : base(options, registry)
        {
        }

        public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            base.ConfigureServices(services, configuration);
            services.AddScoped<ICredentialValidator<TCredentials>, TValidator>();
        }
    }
}
