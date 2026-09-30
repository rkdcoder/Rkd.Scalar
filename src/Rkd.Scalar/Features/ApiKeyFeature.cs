using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.OpenApi;
using Rkd.Scalar.Security.ApiKey;
using Rkd.Scalar.Security.Contracts;

namespace Rkd.Scalar.Features
{
    internal sealed class ApiKeyFeature<TValidator> : IScalarFeature
        where TValidator : class, ICredentialValidator<ApiKeyCredentials>
    {
        private readonly Action<ApiKeyAuthenticationOptions>? _configure;

        private readonly bool _registerValidator;

        public ApiKeyFeature(
            Action<ApiKeyAuthenticationOptions>? configure = null,
            bool registerValidator = true)
        {
            _configure = configure;
            _registerValidator = registerValidator;
        }

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            if (_registerValidator)
                services.AddScoped<TValidator>();

            services.AddAuthentication()
                .AddScheme<ApiKeyAuthenticationOptions,
                    ApiKeyAuthenticationHandler<TValidator>>(
                        RkdScalarAuthenticationSchemes.ApiKey,
                        options =>
                        {
                            _configure?.Invoke(options);

                            if (string.IsNullOrWhiteSpace(options.HeaderName))
                                throw new InvalidOperationException("ApiKeyAuthenticationOptions.HeaderName cannot be empty.");
                        });

            services.ConfigureAll<OpenApiOptions>(options =>
            {
                options.AddDocumentTransformer<ApiKeySecurityTransformer>();
            });
        }

        public void ConfigureApp(WebApplication app)
        {
        }
    }
}
