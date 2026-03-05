using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Builder;
using Rkd.Scalar.Configuration;
using Rkd.Scalar.Infrastructure;
using Scalar.AspNetCore;

namespace Rkd.Scalar.Extensions
{

    public static class RkdScalarExtensions
    {
        public static ScalarBuilder AddRkdScalar(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddOpenApi();

            var registry = new ScalarFeatureRegistry();

            services.AddSingleton(registry);

            return new ScalarBuilder(
                services,
                configuration,
                registry);
        }

        public static IApplicationBuilder UseRkdScalar(
           this WebApplication app,
           RkdScalarConfiguration options)
        {
            var registry =
                app.Services.GetRequiredService<ScalarFeatureRegistry>();

            foreach (var feature in registry.Features)
            {
                feature.ConfigureApp(app);
            }

            app.MapOpenApi(options.OpenApiRoutePattern);

            var provider =
                app.Services.GetService<IApiVersionDescriptionProvider>();

            app.MapScalarApiReference(opt =>
            {
                opt.Title = options.Title;
                opt.Theme = options.Theme;

                options.ConfigureScalar?.Invoke(opt);

                if (provider != null)
                {
                    foreach (var description in provider.ApiVersionDescriptions)
                    {
                        opt.AddDocument(description.GroupName);
                    }
                }
                else
                {
                    opt.AddDocument("v1");
                }
            });

            ReservedRouteGuard.EnsureControllersDoNotUseReservedRoutes(app);

            return app;
        }
    }
}