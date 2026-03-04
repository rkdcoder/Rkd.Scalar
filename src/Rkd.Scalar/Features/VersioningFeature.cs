using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Rkd.Scalar.OpenApi;
using System.Reflection;

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
                    options.GroupNameFormat = "'v'V";
                    options.SubstituteApiVersionInUrl = true;
                });

            var versions = DiscoverVersions();

            foreach (var version in versions)
            {
                services.AddOpenApi(version);
            }
        }

        public void ConfigureApp(WebApplication app)
        {
        }

        private static IEnumerable<string> DiscoverVersions()
        {
            var assembly = Assembly.GetEntryAssembly();

            var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "v1"
            };

            if (assembly == null)
                return versions;

            var controllerTypes = assembly.GetTypes()
                .Where(t =>
                    typeof(ControllerBase).IsAssignableFrom(t) &&
                    !t.IsAbstract);

            foreach (var controller in controllerTypes)
            {
                var attrs = controller
                    .GetCustomAttributes<ApiVersionAttribute>();

                foreach (var attr in attrs)
                {
                    foreach (var v in attr.Versions)
                    {
                        versions.Add($"v{v.MajorVersion}");
                    }
                }
            }

            return versions;
        }
    }

    internal sealed class VersionedApiDocumentTransformer : IOpenApiDocumentTransformer
    {
        private readonly IApiDescriptionGroupCollectionProvider _provider;

        public VersionedApiDocumentTransformer(
            IApiDescriptionGroupCollectionProvider provider)
        {
            _provider = provider;
        }

        public Task TransformAsync(
            OpenApiDocument document,
            OpenApiDocumentTransformerContext context,
            CancellationToken cancellationToken)
        {
            var group = _provider.ApiDescriptionGroups.Items
                .FirstOrDefault(g => g.GroupName == context.DocumentName);

            if (group == null)
                return Task.CompletedTask;

            var validPaths = group.Items
                .Select(a =>
                {
                    var path = "/" + a.RelativePath!.TrimStart('/');
                    return path.ToLowerInvariant();
                })
                .ToHashSet();

            var remove = document.Paths
                .Where(p => !validPaths.Contains(p.Key.ToLowerInvariant()))
                .Select(p => p.Key)
                .ToList();

            foreach (var path in remove)
            {
                document.Paths.Remove(path);
            }

            return Task.CompletedTask;
        }
    }
}
