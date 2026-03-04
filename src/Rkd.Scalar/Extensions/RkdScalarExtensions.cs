using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Builder;
using Rkd.Scalar.Configuration;
using Rkd.Scalar.Features;
using Rkd.Scalar.OpenApi;
using Scalar.AspNetCore;

namespace Rkd.Scalar.Extensions
{

    public static class RkdScalarExtensions
    {
        public static ScalarBuilder AddRkdScalar(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var builder = new ScalarBuilder(services, configuration);

            services.AddSingleton(builder);

            services.AddOpenApi();

            return builder;
        }

        public static IApplicationBuilder UseRkdScalar(
            this WebApplication app,
            RkdScalarConfiguration options)
        {
            var features = app.Services.GetServices<IScalarFeature>();

            foreach (var feature in features)
            {
                feature.ConfigureApp(app);
            }

            app.MapOpenApi(options.OpenApiRoutePattern);

            var provider =
                app.Services.GetService<IApiVersionDescriptionProvider>();

            app.MapScalarApiReference(opt =>
            {
                opt.Title = options.Title;

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

            return app;
        }
    }
}