using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rkd.Scalar.Features;
using Rkd.Scalar.Infrastructure;
using Rkd.Scalar.Security.Configuration;
using Rkd.Scalar.Security.Contracts;
using Rkd.Scalar.Security.Wrappers;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Rkd.Scalar
{
    /// <summary>
    /// Fluent builder returned by <c>AddRkdScalar</c> to enable Rkd.Scalar features.
    /// </summary>
    public sealed class RkdScalarBuilder
    {
        /// <summary>Default configuration section of <see cref="JwtOptions"/>.</summary>
        public const string DefaultJwtSection = "Jwt";

        /// <summary>The application service collection.</summary>
        public IServiceCollection Services { get; }

        /// <summary>The application configuration.</summary>
        public IConfiguration Configuration { get; }

        private readonly ScalarFeatureRegistry _registry;

        internal RkdScalarBuilder(
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

        #region Scalar UI protection

        /// <summary>
        /// Protects the Scalar UI and the OpenAPI documents with Basic Authentication, validated by
        /// <typeparamref name="TValidator"/>. API endpoints are not affected.
        /// </summary>
        /// <typeparam name="TValidator">Validator of <see cref="BasicAuthCredentials"/>.</typeparam>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithUiProtection<TValidator>()
            where TValidator : class, ICredentialValidator<BasicAuthCredentials>
        {
            Services.AddScoped<TValidator>();
            Services.AddScoped<IUiCredentialValidator, UiCredentialValidatorWrapper<TValidator>>();

            RegisterFeature(new UiProtectionFeature());

            return this;
        }

        /// <summary>
        /// Protects the Scalar UI and the OpenAPI documents with Basic Authentication, validating the
        /// credentials stored in a configuration section — no validator class required.
        /// </summary>
        /// <param name="sectionName">
        /// Section containing <c>{ "Username": "...", "Password": "..." }</c> or a <c>{ "user": "password" }</c> map.
        /// Defaults to <c>UiCredentials</c>. The application fails at startup when it is missing or empty.
        /// </param>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithUiProtection(string sectionName = "UiCredentials")
        {
            Services.AddSingleton<IUiCredentialValidator>(new UiConfigurationCredentialValidator(
                ConfigurationCredentialValidator.FromSection(Configuration, sectionName)));

            RegisterFeature(new UiProtectionFeature());

            return this;
        }

        /// <summary>
        /// Protects the Scalar UI and the OpenAPI documents with Basic Authentication using a single fixed user.
        /// </summary>
        /// <param name="username">Username required to open the documentation.</param>
        /// <param name="password">Password required to open the documentation. Load it from a secret store.</param>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithUiProtection(string username, string password)
        {
            Services.AddSingleton<IUiCredentialValidator>(new UiConfigurationCredentialValidator(
                ConfigurationCredentialValidator.FromUser(username, password)));

            RegisterFeature(new UiProtectionFeature());

            return this;
        }

        #endregion

        #region Basic and API Key

        /// <summary>
        /// Enables HTTP Basic authentication for the API, validated by <typeparamref name="TValidator"/>.
        /// </summary>
        /// <typeparam name="TValidator">Validator of <see cref="BasicAuthCredentials"/>.</typeparam>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithBasicAuth<TValidator>()
            where TValidator : class, ICredentialValidator<BasicAuthCredentials>
        {
            Services.AddScoped<TValidator>();

            _registry.AddAuthenticationScheme(RkdScalarAuthenticationSchemes.Basic);
            RegisterFeature(new BasicAuthFeature<TValidator>());

            return this;
        }

        /// <summary>
        /// Enables HTTP Basic authentication for the API, validating the users stored in a configuration
        /// section — no validator class required.
        /// </summary>
        /// <param name="sectionName">
        /// Section containing <c>{ "Username": "...", "Password": "..." }</c> or a <c>{ "user": "password" }</c> map.
        /// Defaults to <c>BasicAuth</c>.
        /// </param>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithBasicAuth(string sectionName = "BasicAuth")
        {
            Services.AddSingleton(new BasicConfigurationCredentialValidator(
                ConfigurationCredentialValidator.FromSection(Configuration, sectionName)));

            _registry.AddAuthenticationScheme(RkdScalarAuthenticationSchemes.Basic);
            RegisterFeature(new BasicAuthFeature<BasicConfigurationCredentialValidator>());

            return this;
        }

        /// <summary>
        /// Enables API Key authentication for the API, validated by <typeparamref name="TValidator"/>.
        /// </summary>
        /// <typeparam name="TValidator">Validator of <see cref="ApiKeyCredentials"/>.</typeparam>
        /// <param name="configure">Optional options, e.g. a different header name.</param>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithApiKeyAuth<TValidator>(Action<ApiKeyAuthenticationOptions>? configure = null)
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
        /// Section containing <c>{ "client-name": "api-key" }</c>, <c>[ "api-key" ]</c> or
        /// <c>{ "client-name": { "Key": "api-key", "Roles": [ "role" ] } }</c>. Defaults to <c>ApiKeys</c>.
        /// </param>
        /// <param name="configure">Optional options, e.g. a different header name.</param>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithApiKeyAuth(
            string sectionName = "ApiKeys",
            Action<ApiKeyAuthenticationOptions>? configure = null)
        {
            Services.AddSingleton(new ConfigurationApiKeyValidator(Configuration, sectionName));

            _registry.AddAuthenticationScheme(RkdScalarAuthenticationSchemes.ApiKey);
            RegisterFeature(new ApiKeyFeature<ConfigurationApiKeyValidator>(configure, registerValidator: false));

            return this;
        }

        #endregion

        #region JWT

        /// <summary>
        /// Enables JWT Bearer validation only (tokens issued elsewhere: an identity provider, another service).
        /// </summary>
        /// <param name="options">Validation options.</param>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithBearerAuth(JwtOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            options.Validate(nameof(JwtOptions));

            _registry.AddAuthenticationScheme(RkdScalarAuthenticationSchemes.Bearer);
            RegisterFeature(new BearerAuthFeature(options));

            return this;
        }

        /// <summary>
        /// Enables JWT Bearer validation only, binding <see cref="JwtOptions"/> from a configuration section.
        /// </summary>
        /// <param name="sectionName">Configuration section. Defaults to <c>Jwt</c>.</param>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithBearerAuth(string sectionName = DefaultJwtSection)
        {
            return WithBearerAuth(BindJwtOptions(sectionName));
        }

        /// <summary>
        /// Enables JWT Bearer validation and token issuing: registers <typeparamref name="TValidator"/> and
        /// <see cref="IJwtTokenService"/> for your login endpoint (or <see cref="WithJwtLoginEndpoint{TCredentials}(string, int, TimeSpan?)"/>).
        /// </summary>
        /// <typeparam name="TCredentials">Login request model.</typeparam>
        /// <typeparam name="TValidator">Validator of <typeparamref name="TCredentials"/>.</typeparam>
        /// <param name="options">Signing and validation options.</param>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithBearerAuth<TCredentials, TValidator>(JwtOptions options)
            where TCredentials : class
            where TValidator : class, ICredentialValidator<TCredentials>
        {
            ArgumentNullException.ThrowIfNull(options);
            options.Validate(nameof(JwtOptions));

            _registry.AddAuthenticationScheme(RkdScalarAuthenticationSchemes.Bearer);
            RegisterFeature(new BearerAuthFeature<TCredentials, TValidator>(options));

            return this;
        }

        /// <summary>
        /// Enables JWT Bearer validation and token issuing, binding <see cref="JwtOptions"/> from a configuration section.
        /// </summary>
        /// <typeparam name="TCredentials">Login request model.</typeparam>
        /// <typeparam name="TValidator">Validator of <typeparamref name="TCredentials"/>.</typeparam>
        /// <param name="sectionName">Configuration section. Defaults to <c>Jwt</c>.</param>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithBearerAuth<TCredentials, TValidator>(string sectionName = DefaultJwtSection)
            where TCredentials : class
            where TValidator : class, ICredentialValidator<TCredentials>
        {
            return WithBearerAuth<TCredentials, TValidator>(BindJwtOptions(sectionName));
        }

        private JwtOptions BindJwtOptions(string sectionName)
        {
            var section = Configuration.GetSection(sectionName);

            if (!section.Exists())
                throw new InvalidOperationException(
                    $"Configuration section '{sectionName}' was not found. " +
                    "Add it to appsettings.json or use the WithBearerAuth(JwtOptions) overload.");

            var options = new JwtOptions();
            section.Bind(options);

            options.Validate($"Configuration section '{sectionName}'");

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
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithJwtSigner<TSigner>()
            where TSigner : class, IJwtSigner
        {
            Services.RemoveAll<IJwtSigner>();
            Services.AddSingleton<IJwtSigner, TSigner>();

            return this;
        }

        /// <summary>
        /// Replaces the default <see cref="IJwtTokenService"/>.
        /// </summary>
        /// <typeparam name="TService">Your <see cref="IJwtTokenService"/> implementation.</typeparam>
        /// <param name="lifetime">Service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithJwtTokenService<TService>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
            where TService : class, IJwtTokenService
        {
            Services.RemoveAll<IJwtTokenService>();
            Services.Add(new ServiceDescriptor(typeof(IJwtTokenService), typeof(TService), lifetime));

            return this;
        }

        /// <summary>
        /// Publishes the public JWT signing keys as a JSON Web Key Set, so other services can validate
        /// the tokens issued by this API. Requires an asymmetric key; HMAC secrets are never published.
        /// </summary>
        /// <param name="path">Route of the key set. Defaults to <c>/.well-known/jwks.json</c>.</param>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithJwksEndpoint(string path = "/.well-known/jwks.json")
        {
            RegisterFeature(new JwksEndpointFeature(path));

            return this;
        }

        /// <summary>
        /// Maps a ready-made login endpoint that validates <typeparamref name="TCredentials"/> and returns a JWT
        /// (<see cref="JwtLoginResponse"/>), protected by a fixed-window rate limiter partitioned per client IP.
        /// Requires <c>app.UseRateLimiter()</c> and <c>WithBearerAuth&lt;TCredentials, TValidator&gt;()</c>.
        /// </summary>
        /// <typeparam name="TCredentials">Login request model (same type used in <c>WithBearerAuth</c>).</typeparam>
        /// <param name="path">Route of the endpoint. Defaults to <c>/auth/login</c>.</param>
        /// <param name="permitLimit">Attempts allowed per client IP within <paramref name="window"/>. Defaults to 5.</param>
        /// <param name="window">Rate limiting window. Defaults to one minute.</param>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithJwtLoginEndpoint<TCredentials>(
            string path = "/auth/login",
            int permitLimit = 5,
            TimeSpan? window = null)
            where TCredentials : class
        {
            RegisterFeature(new JwtLoginEndpointFeature<TCredentials>(
                path,
                permitLimit,
                window ?? TimeSpan.FromMinutes(1)));

            return this;
        }

        /// <summary>
        /// Maps a ready-made login endpoint protected by a rate limiting policy you registered with <c>AddRateLimiter</c>.
        /// </summary>
        /// <typeparam name="TCredentials">Login request model (same type used in <c>WithBearerAuth</c>).</typeparam>
        /// <param name="path">Route of the endpoint.</param>
        /// <param name="rateLimitPolicy">Name of an existing rate limiting policy.</param>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithJwtLoginEndpoint<TCredentials>(string path, string rateLimitPolicy)
            where TCredentials : class
        {
            RegisterFeature(new JwtLoginEndpointFeature<TCredentials>(path, rateLimitPolicy));

            return this;
        }

        #endregion

        #region Authentication behavior

        /// <summary>
        /// Registers a default authentication scheme that forwards each request to Bearer, Basic or API Key
        /// according to the credentials it carries, so a plain <c>[Authorize]</c> or <c>RequireAuthorization()</c>
        /// accepts every scheme enabled in Rkd.Scalar.
        /// </summary>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithDefaultAuthenticationScheme()
        {
            RegisterFeature(new DefaultAuthenticationSchemeFeature());

            return this;
        }

        /// <summary>
        /// Documents security requirements per operation instead of globally: only endpoints that require
        /// authorization show the lock in Scalar, and <c>[Authorize(AuthenticationSchemes = ...)]</c> restricts the
        /// schemes offered.
        /// </summary>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithOperationSecurity()
        {
            if (_registry.OperationLevelSecurity)
                return this;

            _registry.OperationLevelSecurity = true;
            RegisterFeature(new OperationSecurityFeature());

            return this;
        }

        #endregion

        #region Errors

        /// <summary>
        /// Standardizes every error response as RFC 9457 problem details (<c>application/problem+json</c>):
        /// unhandled exceptions, mapped exceptions, <see cref="ProblemException"/>, and body-less error responses
        /// (404, 405, 401/403…). Every response carries <c>status</c>, <c>title</c>, <c>type</c>, <c>instance</c> and <c>traceId</c>.
        /// </summary>
        /// <param name="configure">Exception mappings and options.</param>
        /// <remarks>
        /// Safe by default: outside Development, 500 responses never include exception messages or stack traces.
        /// The middleware is placed at the beginning of the pipeline automatically.
        /// </remarks>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithProblemDetails(Action<RkdProblemDetailsOptions>? configure = null)
        {
            if (_registry.Features.OfType<ProblemDetailsFeature>().Any())
                throw new InvalidOperationException("WithProblemDetails can only be called once.");

            var options = new RkdProblemDetailsOptions();
            configure?.Invoke(options);

            RegisterFeature(new ProblemDetailsFeature(options));

            return this;
        }

        #endregion

        #region JSON

        /// <summary>
        /// Applies the same JSON naming policy everywhere: controller responses (MVC), minimal APIs, the OpenAPI
        /// schemas shown and sent by Scalar, dictionary keys and model validation error names.
        /// </summary>
        /// <param name="namingPolicy">For example <see cref="JsonNamingPolicy.SnakeCaseLower"/> or <see cref="JsonNamingPolicy.CamelCase"/>.</param>
        /// <param name="applyToDictionaryKeys">Also applies the policy to dictionary keys. Defaults to <see langword="true"/>.</param>
        /// <remarks>
        /// ASP.NET Core keeps two independent JSON settings — <c>AddJsonOptions</c> (controllers) and
        /// <c>ConfigureHttpJsonOptions</c> (minimal APIs and OpenAPI) — so configuring only one of them makes the
        /// documentation disagree with the real payloads. This method configures both.
        /// </remarks>
        /// <example><code>.WithJsonNaming(JsonNamingPolicy.SnakeCaseLower)   // unit_price, created_at…</code></example>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithJsonNaming(JsonNamingPolicy namingPolicy, bool applyToDictionaryKeys = true)
        {
            ArgumentNullException.ThrowIfNull(namingPolicy);

            ConfigureJson(json =>
            {
                json.PropertyNamingPolicy = namingPolicy;

                if (applyToDictionaryKeys)
                    json.DictionaryKeyPolicy = namingPolicy;
            });

            // Validation errors are keyed by the JSON names ("unit_price") instead of the C# names ("UnitPrice").
            Services.Configure<MvcOptions>(options =>
                options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider(namingPolicy)));

            return this;
        }

        /// <summary>
        /// Configures the JSON serializer of controllers (MVC) and of minimal APIs / OpenAPI at once, so responses,
        /// requests and the schemas documented in Scalar always match.
        /// </summary>
        /// <param name="configure">Configures <see cref="JsonSerializerOptions"/>; runs once for each of the two settings.</param>
        /// <example>
        /// <code>
        /// .ConfigureJson(json =>
        /// {
        ///     json.Converters.Add(new JsonStringEnumConverter());
        ///     json.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        /// })
        /// </code>
        /// </example>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder ConfigureJson(Action<JsonSerializerOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            Services.ConfigureHttpJsonOptions(options => configure(options.SerializerOptions));
            Services.Configure<MvcJsonOptions>(options => configure(options.JsonSerializerOptions));

            return this;
        }

        #endregion

        #region OpenAPI and routing

        /// <summary>
        /// Enables API versioning and one OpenAPI document per version.
        /// </summary>
        /// <param name="versions">Versions to document, e.g. <c>"v1", "v2"</c>.</param>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        /// <exception cref="ArgumentException">Thrown when no version is provided.</exception>
        public RkdScalarBuilder WithVersioning(params string[] versions)
        {
            return WithVersioning(configure: null, versions);
        }

        /// <summary>
        /// Enables API versioning and one OpenAPI document per version, customizing <see cref="ApiVersioningOptions"/>.
        /// </summary>
        /// <param name="configure">Applied after the Rkd.Scalar defaults (default version 1.0, assume default, report versions).</param>
        /// <param name="versions">Versions to document, e.g. <c>"v1", "v2"</c>.</param>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        /// <exception cref="ArgumentException">Thrown when no version is provided.</exception>
        public RkdScalarBuilder WithVersioning(Action<ApiVersioningOptions>? configure, params string[] versions)
        {
            if (versions == null || versions.Length == 0)
                throw new ArgumentException("At least one version must be provided. Ex: .WithVersioning(\"v1\") or .WithVersioning(\"v1\", \"v2\", \"v3\")");

            RegisterFeature(new VersioningFeature(versions, configure));

            return this;
        }

        /// <summary>
        /// Configures the <see cref="ApiModuleAttribute"/> controllers, e.g. a route template without the
        /// <c>api</c> prefix. Not required to use <c>[ApiModule]</c>.
        /// </summary>
        /// <param name="configure">Configures <see cref="ApiModuleOptions"/>.</param>
        /// <example><code>.WithApiModules(o => o.RouteTemplate = "v{version:apiVersion}/[module]/[controller]")</code></example>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithApiModules(Action<ApiModuleOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            configure(_registry.ApiModules);

            return this;
        }

        /// <summary>
        /// Enables or disables XML documentation comments in the OpenAPI documents (enabled by default).
        /// Requires <c>&lt;GenerateDocumentationFile&gt;true&lt;/GenerateDocumentationFile&gt;</c> in your projects.
        /// </summary>
        /// <param name="enabled"><see langword="false"/> to turn XML comments off.</param>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithXmlComments(bool enabled = true)
        {
            _registry.XmlComments = enabled;

            return this;
        }

        /// <summary>
        /// Customizes every OpenAPI document (all versions), e.g. to add your own transformers.
        /// </summary>
        /// <param name="configure">Configures <see cref="OpenApiOptions"/>.</param>
        /// <example><code>.ConfigureOpenApi(o => o.AddSchemaTransformer&lt;MySchemaTransformer&gt;())</code></example>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder ConfigureOpenApi(Action<OpenApiOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            Services.ConfigureAll(configure);

            return this;
        }

        /// <summary>
        /// Configures ASP.NET routing to generate lowercase URLs and query strings.
        /// </summary>
        /// <returns>The current <see cref="RkdScalarBuilder"/> instance.</returns>
        public RkdScalarBuilder WithLowercaseRouting()
        {
            Services.Configure<RouteOptions>(options =>
            {
                options.LowercaseUrls = true;
                options.LowercaseQueryStrings = true;
            });

            return this;
        }

        #endregion
    }
}
