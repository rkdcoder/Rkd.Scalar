using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rkd.Scalar.Builder;
using Rkd.Scalar.Configuration;
using Rkd.Scalar.Infrastructure;
using Rkd.Scalar.OpenApi.XmlComments;
using Scalar.AspNetCore;

namespace Rkd.Scalar.Extensions
{
    /// <summary>
    /// Entry points to register and map Rkd.Scalar.
    /// </summary>
    public static class RkdScalarExtensions
    {
        /// <summary>
        /// Default configuration section read by <see cref="UseRkdScalar(WebApplication)"/>.
        /// </summary>
        public const string DefaultConfigurationSection = "RkdScalar";

        /// <summary>
        /// Registers OpenAPI and returns the Rkd.Scalar fluent builder.
        /// </summary>
        public static ScalarBuilder AddRkdScalar(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddOpenApi();

            var registry = new ScalarFeatureRegistry();

            services.AddSingleton(registry);

            // XML comments for every OpenAPI document (see XmlDocumentationProvider for why this is done at runtime).
            services.AddSingleton<XmlDocumentationProvider>();
            services.ConfigureAll<OpenApiOptions>(options =>
            {
                options.AddOperationTransformer<XmlCommentOperationTransformer>();
                options.AddSchemaTransformer<XmlCommentSchemaTransformer>();
            });

            return new ScalarBuilder(
                services,
                configuration,
                registry);
        }

        /// <summary>
        /// Registers OpenAPI and returns the Rkd.Scalar fluent builder, using the host services and configuration.
        /// </summary>
        /// <example><code>builder.AddRkdScalar().WithVersioning("v1").WithBearerAuth();</code></example>
        public static ScalarBuilder AddRkdScalar(this IHostApplicationBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            return builder.Services.AddRkdScalar(builder.Configuration);
        }

        /// <summary>
        /// Maps the OpenAPI documents, the Scalar UI and every enabled feature, reading
        /// <see cref="RkdScalarConfiguration"/> from the <c>RkdScalar</c> configuration section when present.
        /// </summary>
        public static IApplicationBuilder UseRkdScalar(this WebApplication app)
        {
            return app.UseRkdScalar(BindConfiguration(app, DefaultConfigurationSection));
        }

        /// <summary>
        /// Maps the OpenAPI documents, the Scalar UI and every enabled feature. The configuration is read
        /// from <paramref name="sectionName"/> when present and then customized by <paramref name="configure"/>.
        /// </summary>
        /// <example><code>app.UseRkdScalar("RkdScalar", o => o.ConfigureScalar = s => s.DarkMode = true);</code></example>
        public static IApplicationBuilder UseRkdScalar(
            this WebApplication app,
            string sectionName,
            Action<RkdScalarConfiguration>? configure = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

            var options = BindConfiguration(app, sectionName);

            configure?.Invoke(options);

            return app.UseRkdScalar(options);
        }

        /// <summary>
        /// Maps the OpenAPI documents, the Scalar UI and every enabled feature.
        /// </summary>
        public static IApplicationBuilder UseRkdScalar(
           this WebApplication app,
           RkdScalarConfiguration options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var registry =
                app.Services.GetRequiredService<ScalarFeatureRegistry>();

            registry.Configuration = options;

            foreach (var feature in registry.Features)
            {
                feature.ConfigureApp(app);
            }

            if (!options.Enabled)
                return app;

            app.MapOpenApi(options.OpenApiRoutePattern);

            var provider =
                app.Services.GetService<IApiVersionDescriptionProvider>();

            if (options.VersionSelector)
            {
                ScalarDocumentSelector.UseVersionRedirect(app, options.ScalarRoutePrefix);

                app.MapScalarApiReference(options.ScalarRoutePrefix, (opt, context) =>
                {
                    ApplyScalarOptions(opt, options);
                    ScalarDocumentSelector.AddDocuments(opt, context);
                });
            }
            else
            {
                app.MapScalarApiReference(options.ScalarRoutePrefix, opt =>
                {
                    ApplyScalarOptions(opt, options);

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
            }

            ReservedRouteGuard.EnsureControllersDoNotUseReservedRoutes(app);

            return app;
        }

        private static void ApplyScalarOptions(ScalarOptions scalar, RkdScalarConfiguration options)
        {
            scalar.Title = options.Title;
            scalar.Theme = options.Theme;
            scalar.OpenApiRoutePattern = options.OpenApiRoutePattern;

            options.ConfigureScalar?.Invoke(scalar);
        }

        private static RkdScalarConfiguration BindConfiguration(WebApplication app, string sectionName)
        {
            var options = new RkdScalarConfiguration();

            var section = app.Configuration.GetSection(sectionName);

            if (section.Exists())
                section.Bind(options);

            return options;
        }
    }
}
