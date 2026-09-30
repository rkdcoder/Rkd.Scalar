using Asp.Versioning;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rkd.Scalar.Features;
using Rkd.Scalar.Infrastructure;
using Rkd.Scalar.Security;
using Rkd.Scalar.Security.ApiKey;
using Rkd.Scalar.Security.Basic;
using Rkd.Scalar.Security.Configuration;
using Rkd.Scalar.Security.Contracts;
using Rkd.Scalar.Security.Jwt;
using Rkd.Scalar.Security.Wrappers;

namespace Rkd.Scalar.Builder
{
    /// <summary>
    /// Fluent builder returned by <c>AddRkdScalar</c> to enable Rkd.Scalar features.
    /// </summary>
    public sealed class ScalarBuilder
    {
        /// <summary>The application service collection.</summary>
        public IServiceCollection Services { get; }

        /// <summary>The application configuration.</summary>
        public IConfiguration Configuration { get; }

        private readonly ScalarFeatureRegistry _registry;

        internal ScalarBuilder(
            IServiceCollection services,
            IConfiguration configuration,
            ScalarFeatureRegistry registry)
        {
            Services = services;
            Configuration = configuration;
            _registry = registry;
        }

        internal void RegisterFeature(IScalarFeature feature)
        {
            _registry.Features.Add(feature);

            feature.ConfigureServices(Services, Configuration);
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
        /// Protects the Scalar UI with Basic Authentication, validating the credentials stored in
        /// a configuration section — no validator class required.
        /// </summary>
        /// <param name="sectionName">
        /// Configuration section containing either <c>{ "Username": "...", "Password": "..." }</c>
        /// or a <c>{ "user": "password" }</c> map. Defaults to <c>UiCredentials</c>.
        /// </param>
        /// <remarks>
        /// Usernames are case-insensitive and passwords are compared in constant time.
        /// The application fails at startup when the section is missing or empty.
        /// </remarks>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithUiProtection(string sectionName = "UiCredentials")
        {
            var validator = new UiConfigurationCredentialValidator(
                ConfigurationCredentialValidator.FromSection(Configuration, sectionName));

            Services.AddSingleton<IUiCredentialValidator>(validator);

            RegisterFeature(new UiProtectionFeature());

            return this;
        }

        /// <summary>
        /// Protects the Scalar UI with Basic Authentication using a single fixed user.
        /// </summary>
        /// <param name="username">Username required to open the documentation.</param>
        /// <param name="password">Password required to open the documentation. Load it from a secret store.</param>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithUiProtection(string username, string password)
        {
            var validator = new UiConfigurationCredentialValidator(
                ConfigurationCredentialValidator.FromUser(username, password));

            Services.AddSingleton<IUiCredentialValidator>(validator);

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

            _registry.AddAuthenticationScheme(RkdScalarAuthenticationSchemes.Basic);
            RegisterFeature(new BasicAuthFeature<TValidator>());

            return this;
        }

        /// <summary>
        /// Enables Basic Authentication for API endpoints, validating the users stored in a configuration
        /// section — no validator class required.
        /// </summary>
        /// <param name="sectionName">
        /// Configuration section containing either <c>{ "Username": "...", "Password": "..." }</c>
        /// or a <c>{ "user": "password" }</c> map. Defaults to <c>BasicAuth</c>.
        /// </param>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithBasicAuth(string sectionName = "BasicAuth")
        {
            Services.AddSingleton(new BasicConfigurationCredentialValidator(
                ConfigurationCredentialValidator.FromSection(Configuration, sectionName)));

            _registry.AddAuthenticationScheme(RkdScalarAuthenticationSchemes.Basic);
            RegisterFeature(new BasicAuthFeature<BasicConfigurationCredentialValidator>());

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
        /// Configuration options used to generate and validate JWT tokens
        /// (HMAC secret, RSA/ECDSA keys, <c>SecurityKey</c> or OIDC authority).
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
            _registry.AddAuthenticationScheme(RkdScalarAuthenticationSchemes.Bearer);
            RegisterFeature(new BearerAuthFeature<TCredential, TValidator>(options));

            return this;
        }

        /// <summary>
        /// Enables JWT Bearer Authentication support for token validation only,
        /// without requiring a login credential model/validator.
        /// </summary>
        /// <param name="options">
        /// Configuration options used to validate JWT tokens.
        /// </param>
        /// <remarks>
        /// Use this overload when your API only validates bearer tokens
        /// and does not expose a JWT login endpoint via Rkd.Scalar.
        /// </remarks>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithBearerAuth(JwtOptions options)
        {
            _registry.AddAuthenticationScheme(RkdScalarAuthenticationSchemes.Bearer);
            RegisterFeature(new BearerAuthFeature(options));
            return this;
        }

        /// <summary>
        /// Enables JWT Bearer Authentication, binding <see cref="JwtOptions"/>
        /// from the given configuration section (appsettings.json).
        /// </summary>
        public ScalarBuilder WithBearerAuth<TCredential, TValidator>(
            string sectionName = "JwtOptions")
            where TCredential : class
            where TValidator : class, ICredentialValidator<TCredential>
        {
            return WithBearerAuth<TCredential, TValidator>(
                BindJwtOptions(sectionName));
        }

        /// <summary>
        /// Enables JWT Bearer Authentication (validation-only mode), binding
        /// <see cref="JwtOptions"/> from the given configuration section.
        /// </summary>
        public ScalarBuilder WithBearerAuth(string sectionName = "JwtOptions")
        {
            return WithBearerAuth(BindJwtOptions(sectionName));
        }

        private JwtOptions BindJwtOptions(string sectionName)
        {
            var section = Configuration.GetSection(sectionName);

            if (!section.Exists())
                throw new InvalidOperationException(
                    $"Configuration section '{sectionName}' was not found. " +
                    "Add it to appsettings.json or use the WithBearerAuth(JwtOptions) overload.");

            var settings = section.Get<JwtSettings>()
                ?? throw new InvalidOperationException(
                    $"Configuration section '{sectionName}' could not be bound to JwtSettings.");

            var hasAsymmetricKey =
                !string.IsNullOrWhiteSpace(settings.PrivateKeyPem) ||
                !string.IsNullOrWhiteSpace(settings.PrivateKeyPath) ||
                !string.IsNullOrWhiteSpace(settings.PublicKeyPem) ||
                !string.IsNullOrWhiteSpace(settings.PublicKeyPath);

            var hasAuthority =
                !string.IsNullOrWhiteSpace(settings.Authority) ||
                !string.IsNullOrWhiteSpace(settings.MetadataAddress);

            if (string.IsNullOrWhiteSpace(settings.Secret) && !hasAsymmetricKey && !hasAuthority)
                throw new InvalidOperationException(
                    $"'{sectionName}:Secret' is required and cannot be empty " +
                    "(or configure PrivateKeyPem/PrivateKeyPath, PublicKeyPem/PublicKeyPath or Authority).");

            if (settings.Expiration <= 0 && settings.ExpirationMinutes <= 0 && !hasAuthority)
                throw new InvalidOperationException(
                    $"'{sectionName}:Expiration' must be greater than zero (value in hours), " +
                    $"or set '{sectionName}:ExpirationMinutes'.");

            var options = settings.ToJwtOptions();

            if (options.Expiration <= TimeSpan.Zero)
                options.Expiration = TimeSpan.FromHours(1);

            return options;
        }

        /// <summary>
        /// Delegates the JWT signature to <typeparamref name="TSigner"/>, typically a remote key store
        /// (Azure Key Vault, AWS KMS, HSM) where the private key never leaves the vault.
        /// </summary>
        /// <typeparam name="TSigner">Implementation of <see cref="IJwtSigner"/>, registered as a singleton.</typeparam>
        /// <remarks>
        /// Configure the matching public key (<c>PublicKeyPem</c>, <c>PublicKeyPath</c>, <c>ValidationKeys</c>)
        /// or an <c>Authority</c> in <see cref="JwtOptions"/> so the API can validate the tokens it issues.
        /// </remarks>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithJwtSigner<TSigner>()
            where TSigner : class, IJwtSigner
        {
            Services.RemoveAll<IJwtSigner>();
            Services.AddSingleton<IJwtSigner, TSigner>();

            return this;
        }

        /// <summary>
        /// Replaces the default <see cref="IJwtTokenService"/> used to issue tokens
        /// (by your own endpoints and by <c>WithDefaultJwtLogin</c>).
        /// </summary>
        /// <typeparam name="TService">Your <see cref="IJwtTokenService"/> implementation.</typeparam>
        /// <param name="lifetime">Service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithJwtTokenService<TService>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
            where TService : class, IJwtTokenService
        {
            Services.RemoveAll<IJwtTokenService>();
            Services.Add(new ServiceDescriptor(typeof(IJwtTokenService), typeof(TService), lifetime));

            return this;
        }

        /// <summary>
        /// Publishes the public JWT signing keys as a JSON Web Key Set (JWKS), so other services can
        /// validate the tokens issued by this API.
        /// </summary>
        /// <param name="path">Route of the key set. Defaults to <c>/.well-known/jwks.json</c>.</param>
        /// <remarks>
        /// Requires <c>WithBearerAuth</c> configured with an asymmetric key. HMAC secrets are never published.
        /// </remarks>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithJwksEndpoint(string path = "/.well-known/jwks.json")
        {
            RegisterFeature(new JwksEndpointFeature(path));

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
            return WithApiKeyAuth<TValidator>(configure: null);
        }

        /// <summary>
        /// Enables API Key authentication support for the API, with custom options
        /// (for example a different header name).
        /// </summary>
        /// <typeparam name="TValidator">
        /// Implementation of <see cref="ICredentialValidator{T}"/> responsible for validating
        /// <see cref="ApiKeyCredentials"/> provided by clients.
        /// </typeparam>
        /// <param name="configure">Configures <see cref="ApiKeyAuthenticationOptions"/>.</param>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithApiKeyAuth<TValidator>(Action<ApiKeyAuthenticationOptions>? configure)
            where TValidator : class, ICredentialValidator<ApiKeyCredentials>
        {
            _registry.AddAuthenticationScheme(RkdScalarAuthenticationSchemes.ApiKey);
            RegisterFeature(new ApiKeyFeature<TValidator>(configure));

            return this;
        }

        /// <summary>
        /// Enables API Key authentication, validating the keys stored in a configuration section —
        /// no validator class required.
        /// </summary>
        /// <param name="sectionName">
        /// Configuration section containing <c>{ "client-name": "api-key" }</c>, <c>[ "api-key" ]</c> or
        /// <c>{ "client-name": { "Key": "api-key", "Roles": [ "role" ] } }</c>. Defaults to <c>ApiKeys</c>.
        /// </param>
        /// <param name="configure">Optional <see cref="ApiKeyAuthenticationOptions"/> configuration.</param>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithApiKeyAuth(
            string sectionName = "ApiKeys",
            Action<ApiKeyAuthenticationOptions>? configure = null)
        {
            Services.AddSingleton(new ConfigurationApiKeyValidator(Configuration, sectionName));

            _registry.AddAuthenticationScheme(RkdScalarAuthenticationSchemes.ApiKey);
            RegisterFeature(new ApiKeyFeature<ConfigurationApiKeyValidator>(configure, registerValidator: false));

            return this;
        }

        /// <summary>
        /// Registers a default authentication scheme that forwards each request to Bearer, Basic or
        /// API Key according to the credentials it carries, so a plain <c>[Authorize]</c> or
        /// <c>RequireAuthorization()</c> accepts every scheme enabled in Rkd.Scalar.
        /// </summary>
        /// <remarks>
        /// Without it, ASP.NET Core has no default scheme when more than one is registered and
        /// endpoints must list the schemes explicitly
        /// (<c>[Authorize(AuthenticationSchemes = RkdScalarAuthenticationSchemes.All)]</c>).
        /// </remarks>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithDefaultAuthenticationScheme()
        {
            RegisterFeature(new DefaultAuthenticationSchemeFeature());

            return this;
        }

        /// <summary>
        /// Documents security requirements per operation instead of globally: only endpoints that
        /// require authorization show the lock in Scalar, <c>[AllowAnonymous]</c> endpoints do not, and
        /// <c>[Authorize(AuthenticationSchemes = ...)]</c> restricts the schemes offered.
        /// </summary>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithOperationSecurity()
        {
            if (_registry.OperationLevelSecurity)
                return this;

            _registry.OperationLevelSecurity = true;
            RegisterFeature(new OperationSecurityFeature());

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
            return WithVersioning(configure: null, versions);
        }

        /// <summary>
        /// Enables API versioning support, exposes multiple versions in Scalar and lets you customize
        /// <see cref="ApiVersioningOptions"/> (default version, version readers, etc.).
        /// </summary>
        /// <param name="configure">
        /// Applied after the Rkd.Scalar defaults (default version 1.0, assume default when unspecified,
        /// report versions).
        /// </param>
        /// <param name="versions">List of API versions to expose (for example: "v1", "v2").</param>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        /// <exception cref="ArgumentException">Thrown when no version is provided.</exception>
        public ScalarBuilder WithVersioning(
            Action<ApiVersioningOptions>? configure,
            params string[] versions)
        {
            if (versions == null || versions.Length == 0)
                throw new ArgumentException("At least one version must be provided. Ex: .WithVersioning(\"v1\") or .WithVersioning(\"v1\", \"v2\", \"v3\")");

            RegisterFeature(new VersioningFeature(versions, configure));

            return this;
        }

        /// <summary>
        /// Registers a default endpoint for JWT authentication.
        /// </summary>
        /// <typeparam name="TCredential">
        /// Credential model expected in the login request body.
        /// </typeparam>
        /// <param name="path">
        /// Route where the login endpoint will be exposed.
        /// </param>
        /// <param name="permitLimit">
        /// Maximum number of allowed authentication attempts per client IP within the defined time window.
        /// This value is used to configure a rate limiter to protect the login endpoint
        /// against brute-force attacks.
        /// </param>
        /// <param name="window">
        /// Time window used by the rate limiter to control how many requests are allowed.
        /// </param>
        /// <remarks>
        /// This endpoint validates the provided credentials using the configured
        /// <see cref="ICredentialValidator{T}"/> and returns a generated JWT access token.
        ///
        /// A built-in rate limiter (partitioned per client IP) is automatically configured using the provided
        /// <paramref name="permitLimit"/> and <paramref name="window"/> parameters in order
        /// to prevent excessive authentication attempts. Requires <c>app.UseRateLimiter()</c>.
        ///
        /// The credential type used here must match the credential type configured in
        /// <c>WithBearerAuth&lt;TCredential, TValidator&gt;()</c>.
        /// </remarks>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithDefaultJwtLogin<TCredential>(
            string path,
            int permitLimit,
            TimeSpan window)
            where TCredential : class
        {
            RegisterFeature(
                new DefaultJwtLoginFeature<TCredential>(
                    path,
                    permitLimit,
                    window));

            return this;
        }

        /// <summary>
        /// Registers a default endpoint for JWT authentication protected by a rate limiting policy
        /// you configured yourself with <c>AddRateLimiter</c>.
        /// </summary>
        /// <typeparam name="TCredential">Credential model expected in the login request body.</typeparam>
        /// <param name="path">Route where the login endpoint will be exposed.</param>
        /// <param name="rateLimitPolicy">Name of an existing rate limiting policy.</param>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithDefaultJwtLogin<TCredential>(
            string path,
            string rateLimitPolicy)
            where TCredential : class
        {
            RegisterFeature(
                new DefaultJwtLoginFeature<TCredential>(
                    path,
                    rateLimitPolicy));

            return this;
        }

        /// <summary>
        /// Enables or disables the XML documentation comments (<c>summary</c>, <c>remarks</c>, <c>param</c>,
        /// <c>returns</c>, <c>response</c>) in the OpenAPI documents. Enabled by default.
        /// </summary>
        /// <param name="enabled"><see langword="false"/> to turn XML comments off.</param>
        /// <remarks>
        /// Requires <c>&lt;GenerateDocumentationFile&gt;true&lt;/GenerateDocumentationFile&gt;</c> in the projects
        /// that declare your endpoints and models. The <c>.xml</c> files are read at runtime, so no
        /// <c>AddOpenApi</c> call is needed in your project, and every API version is covered.
        /// </remarks>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder WithXmlComments(bool enabled = true)
        {
            _registry.XmlComments = enabled;

            return this;
        }

        /// <summary>
        /// Customizes every OpenAPI document generated by Rkd.Scalar (all versions), for example to add your
        /// own document, operation or schema transformers.
        /// </summary>
        /// <param name="configure">Configures <see cref="OpenApiOptions"/>.</param>
        /// <example>
        /// <code>.ConfigureOpenApi(o => o.AddSchemaTransformer&lt;MySchemaTransformer&gt;())</code>
        /// </example>
        /// <returns>The current <see cref="ScalarBuilder"/> instance.</returns>
        public ScalarBuilder ConfigureOpenApi(Action<OpenApiOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            Services.ConfigureAll(configure);

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
