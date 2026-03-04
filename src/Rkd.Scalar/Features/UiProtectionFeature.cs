using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rkd.Scalar.Configuration;
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

            app.UseWhen(
                context =>
                {
                    var path = context.Request.Path.Value ?? "";

                    var isScalarUi =
                        path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase) &&
                        !path.StartsWith("/scalar-auth", StringComparison.OrdinalIgnoreCase);

                    var isOpenApi =
                        path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase);

                    return isScalarUi || isOpenApi;
                },
                branch =>
                {
                    branch.UseMiddleware<ScalarUiAuthMiddleware>();
                });
        }
    }
}