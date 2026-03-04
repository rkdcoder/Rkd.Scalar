using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rkd.Scalar.Features
{
    internal sealed class VersioningFeature : IScalarFeature
    {
        private readonly IReadOnlyCollection<string> _versions;

        public VersioningFeature(IEnumerable<string> versions)
        {
            _versions = versions.ToArray();
        }

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
                    options.GroupNameFormat = "'v'V";
                    options.SubstituteApiVersionInUrl = true;
                });

            foreach (var version in _versions)
            {
                services.AddOpenApi(version);
            }
        }

        public void ConfigureApp(WebApplication app)
        {
        }
    }
}
