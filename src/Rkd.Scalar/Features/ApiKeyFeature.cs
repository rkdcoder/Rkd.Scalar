using Microsoft.AspNetCore.Authentication;
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
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<TValidator>();

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions,
                    ApiKeyAuthenticationHandler<TValidator>>("ApiKey", _ => { });

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
