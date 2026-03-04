using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rkd.Scalar.Features
{
    internal sealed class VersioningFeature : IScalarFeature
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services
                .AddApiVersioning(options =>
                {
                    options.DefaultApiVersion = new ApiVersion(1, 0);
                    options.AssumeDefaultVersionWhenUnspecified = true;
                    options.ReportApiVersions = true;
                })
                .AddApiExplorer(options =>
                {
                    options.GroupNameFormat = "'v'VVV";
                    options.SubstituteApiVersionInUrl = true;
                });
        }

        public void ConfigureApp(WebApplication app)
        {
        }
    }
}
