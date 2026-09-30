using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rkd.Scalar.Features
{
    internal sealed class VersioningFeature : IScalarFeature
    {
        private readonly IReadOnlyCollection<string> _versions;

        private readonly Action<ApiVersioningOptions>? _configure;

        public VersioningFeature(
            IEnumerable<string> versions,
            Action<ApiVersioningOptions>? configure = null)
        {
            _versions = versions.ToArray();
            _configure = configure;
        }

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services
                .AddApiVersioning(options =>
                {
                    options.DefaultApiVersion = new ApiVersion(1, 0);
                    options.AssumeDefaultVersionWhenUnspecified = true;
                    options.ReportApiVersions = true;

                    _configure?.Invoke(options);
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
