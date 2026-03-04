using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Features;
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

        public ScalarBuilder WithUiProtection<TValidator>()
            where TValidator : class, ICredentialValidator<BasicAuthCredentials>
        {
            Services.AddScoped<TValidator>();
            Services.AddScoped<IUiCredentialValidator, UiCredentialValidatorWrapper<TValidator>>();

            RegisterFeature(new UiProtectionFeature());

            return this;
        }

        public ScalarBuilder WithBasicAuth<TValidator>()
            where TValidator : class, ICredentialValidator<BasicAuthCredentials>
        {
            Services.AddScoped<TValidator>();

            RegisterFeature(new BasicAuthFeature<TValidator>());

            return this;
        }

        public ScalarBuilder WithBearerAuth<TCredential, TValidator>(JwtOptions options)
            where TCredential : class
            where TValidator : class, ICredentialValidator<TCredential>
        {
            RegisterFeature(new BearerAuthFeature<TCredential, TValidator>(options));

            return this;
        }

        public ScalarBuilder WithAutoVersioning()
        {
            RegisterFeature(new VersioningFeature());

            return this;
        }
    }
}
