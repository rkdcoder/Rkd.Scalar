using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rkd.Scalar.Features
{
    internal sealed class DefaultOpenApiFeature : IScalarFeature
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            if (!services.Any(x => x.ServiceType.Name == "OpenApiDocumentService"))
            {
                services.AddOpenApi("v1");
            }
        }

        public void ConfigureApp(WebApplication app) { }
    }
}
