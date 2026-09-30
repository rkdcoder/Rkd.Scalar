using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.OpenApi;

namespace Rkd.Scalar.Features
{
    internal sealed class OperationSecurityFeature : IScalarFeature
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.ConfigureAll<OpenApiOptions>(options =>
            {
                options.AddOperationTransformer<AuthorizeOperationSecurityTransformer>();
            });
        }

        public void ConfigureApp(WebApplication app)
        {
        }
    }
}
