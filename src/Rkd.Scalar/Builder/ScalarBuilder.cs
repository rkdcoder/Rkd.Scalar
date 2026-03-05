using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Features;
using Rkd.Scalar.Security.ApiKey;
using Rkd.Scalar.Security.Basic;
using Rkd.Scalar.Security.Contracts;
using Rkd.Scalar.Security.Jwt;
using Rkd.Scalar.Security.Wrappers;

namespace Rkd.Scalar.Builder
{
    public sealed class ScalarBuilder
    {
        public IServiceCollection Services { get; }

        public IConfiguration Configuration { get; }

        private readonly List<IScalarFeature> _features = new();

        public ScalarBuilder(IServiceCollection services, IConfiguration configuration)
        {
            Services = services;
            Configuration = configuration;
        }

        internal void RegisterFeature(IScalarFeature feature)
        {
            _features.Add(feature);

            feature.ConfigureServices(Services, Configuration);

            Services.AddSingleton(feature);
        }

        /// <summary>
        /// Protects the Scalar UI with Basic Authentication.
        /// </summary>
        /// <typeparam name="TValidator">
        /// Implementation of <see cref="ICredentialValidator{T}"/> responsible for validating
        /// <see cref="BasicAuthCredentials"/> used to access the Scalar UI.
        /// </typeparam>
        /// <remarks>
        /// This protection applies only to the documentation interface and does not affect API endpoints.
        /// Useful to prevent public access to API documentation in production environments.
        /// </remarks>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithUiProtection<TValidator>()
            where TValidator : class, ICredentialValidator<BasicAuthCredentials>
        {
            Services.AddScoped<TValidator>();
            Services.AddScoped<IUiCredentialValidator, UiCredentialValidatorWrapper<TValidator>>();

            RegisterFeature(new UiProtectionFeature());

            return this;
        }

        /// <summary>
        /// Enables Basic Authentication support for API endpoints documented in Scalar.
        /// </summary>
        /// <typeparam name="TValidator">
        /// Implementation of <see cref="ICredentialValidator{T}"/> responsible for validating
        /// <see cref="BasicAuthCredentials"/> provided by API clients.
        /// </typeparam>
        /// <remarks>
        /// Registers the necessary OpenAPI security scheme and middleware required for
        /// Basic Authentication integration with Scalar.
        /// </remarks>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithBasicAuth<TValidator>()
            where TValidator : class, ICredentialValidator<BasicAuthCredentials>
        {
            Services.AddScoped<TValidator>();

            RegisterFeature(new BasicAuthFeature<TValidator>());

            return this;
        }

        /// <summary>
        /// Enables JWT Bearer Authentication support for the API.
        /// </summary>
        /// <typeparam name="TCredential">
        /// Credential model used to authenticate users when requesting a JWT token.
        /// </typeparam>
        /// <typeparam name="TValidator">
        /// Implementation of <see cref="ICredentialValidator{T}"/> responsible for validating
        /// the credential model before issuing a token.
        /// </typeparam>
        /// <param name="options">
        /// Configuration options used to generate and validate JWT tokens.
        /// </param>
        /// <remarks>
        /// Registers JWT authentication, OpenAPI security definitions and the necessary
        /// middleware to support Bearer token authentication.
        /// </remarks>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithBearerAuth<TCredential, TValidator>(JwtOptions options)
            where TCredential : class
            where TValidator : class, ICredentialValidator<TCredential>
        {
            RegisterFeature(new BearerAuthFeature<TCredential, TValidator>(options));

            return this;
        }

        /// <summary>
        /// Enables API Key authentication support for the API.
        /// </summary>
        /// <typeparam name="TValidator">
        /// Implementation of <see cref="ICredentialValidator{T}"/> responsible for validating
        /// <see cref="ApiKeyCredentials"/> provided by clients.
        /// </typeparam>
        /// <remarks>
        /// Registers the API Key security scheme in OpenAPI and configures the middleware
        /// required for validating incoming API keys.
        /// </remarks>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithApiKeyAuth<TValidator>()
            where TValidator : class, ICredentialValidator<ApiKeyCredentials>
        {
            RegisterFeature(new ApiKeyFeature<TValidator>());

            return this;
        }

        /// <summary>
        /// Enables API versioning support and exposes multiple versions in Scalar.
        /// </summary>
        /// <param name="versions">
        /// List of API versions to expose (for example: "v1", "v2").
        /// </param>
        /// <remarks>
        /// This feature integrates API Versioning with OpenAPI so that each version
        /// is documented and selectable in the Scalar interface.
        /// </remarks>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when no version is provided.
        /// </exception>
        public ScalarBuilder WithVersioning(params string[] versions)
        {
            if (versions == null || versions.Length == 0)
                throw new ArgumentException("At least one version must be provided. Ex: .WithVersioning(\"v1\") or .WithVersioning(\"v1\", \"v2\", \"v3\")");

            RegisterFeature(new VersioningFeature(versions));

            return this;
        }

        /// <summary>
        /// Registers a default endpoint for JWT authentication.
        /// </summary>
        /// <typeparam name="TCredential">
        /// Credential model expected in the login request body.
        /// </typeparam>
        /// <param name="path">
        /// Route where the login endpoint will be exposed. Default: "/default-auth/login".
        /// </param>
        /// <remarks>
        /// This endpoint validates the provided credentials using the configured
        /// <see cref="ICredentialValidator{T}"/> and returns a generated JWT token.
        /// Useful for simple authentication scenarios or development environments.
        /// </remarks>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithDefaultJwtLogin<TCredential>(string path = "/default-auth/login")
            where TCredential : class
        {
            RegisterFeature(new DefaultJwtLoginFeature<TCredential>(path));

            return this;
        }

        /// <summary>
        /// Configures ASP.NET routing to generate lowercase URLs and query strings.
        /// </summary>
        /// <remarks>
        /// Helps maintain consistent and SEO-friendly routes by forcing all generated
        /// URLs to use lowercase characters.
        /// </remarks>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithLowercaseRouting()
        {
            Services.Configure<RouteOptions>(options =>
            {
                options.LowercaseUrls = true;
                options.LowercaseQueryStrings = true;
            });
            return this;
        }
    }
}
