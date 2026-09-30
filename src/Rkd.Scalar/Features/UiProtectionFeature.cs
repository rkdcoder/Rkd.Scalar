using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rkd.Scalar.Infrastructure;
using Rkd.Scalar.Middleware;

namespace Rkd.Scalar.Features
{
    internal sealed class UiProtectionFeature : IScalarFeature
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<ScalarUiProtectionOptions>(o => o.Enabled = true);
        }

        public void ConfigureApp(WebApplication app)
        {
            var options = app.Services
                .GetRequiredService<IOptions<ScalarUiProtectionOptions>>();

            if (!options.Value.Enabled)
                return;

            var configuration = app.Services
                .GetRequiredService<ScalarFeatureRegistry>()
                .Options;

            if (!configuration.Enabled)
                return;

            var scalarPrefix = NormalizePrefix(configuration.ScalarRoutePrefix);
            var openApiPrefix = NormalizePrefix(GetStaticPrefix(configuration.OpenApiRoutePattern));

            app.UseWhen(
                context =>
                {
                    var path = context.Request.Path.Value ?? "";

                    var isScalarUi =
                        path.StartsWith(scalarPrefix, StringComparison.OrdinalIgnoreCase);

                    var isOpenApi =
                        path.StartsWith(openApiPrefix, StringComparison.OrdinalIgnoreCase);

                    return isScalarUi || isOpenApi;
                },
                branch =>
                {
                    branch.UseMiddleware<ScalarUiAuthMiddleware>();
                });
        }

        /// <summary>
        /// Returns the literal part of a route pattern, before its first parameter
        /// ("/openapi/{documentName}.json" → "/openapi/").
        /// </summary>
        internal static string GetStaticPrefix(string routePattern)
        {
            var index = routePattern.IndexOf('{');

            return index < 0 ? routePattern : routePattern[..index];
        }

        internal static string NormalizePrefix(string prefix)
        {
            var normalized = "/" + prefix.Trim().Trim('/');

            if (normalized == "/")
                throw new InvalidOperationException(
                    "The Scalar UI and OpenAPI routes need a non-root prefix to be protected " +
                    "(for example '/scalar' and '/openapi/{documentName}.json').");

            return normalized;
        }
    }
}
