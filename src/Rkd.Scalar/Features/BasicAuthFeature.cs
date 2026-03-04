using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.OpenApi;
using Rkd.Scalar.Security.Basic;
using Rkd.Scalar.Security.Contracts;

namespace Rkd.Scalar.Features
{
    internal sealed class BasicAuthFeature<TValidator> : IScalarFeature
        where TValidator : class, ICredentialValidator<BasicAuthCredentials>
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions,
                    BasicAuthenticationHandler<TValidator>>("Basic", _ => { });

            services.ConfigureAll<OpenApiOptions>(options =>
            {
                options.AddDocumentTransformer<BasicAuthSecurityTransformer>();
            });
        }

        public void ConfigureApp(WebApplication app)
        {
        }
    }
}
