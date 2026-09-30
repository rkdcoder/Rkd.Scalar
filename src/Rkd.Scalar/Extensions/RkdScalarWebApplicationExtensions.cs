using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar;
using Rkd.Scalar.Infrastructure;
using Scalar.AspNetCore;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Maps the Rkd.Scalar documentation and features.
    /// </summary>
    public static class RkdScalarWebApplicationExtensions
    {
        /// <summary>Default configuration section of <see cref="RkdScalarOptions"/>.</summary>
        public const string DefaultConfigurationSection = "RkdScalar";

        /// <summary>
        /// Maps the OpenAPI documents, the Scalar UI and every enabled feature. <see cref="RkdScalarOptions"/>
        /// are read from the <c>RkdScalar</c> configuration section (when present) and then customized by
        /// <paramref name="configure"/>.
        /// </summary>
        /// <example><code>app.UseRkdScalar(o => o.Title = "My API");</code></example>
        public static WebApplication UseRkdScalar(
            this WebApplication app,
            Action<RkdScalarOptions>? configure = null)
        {
            return app.UseRkdScalar(DefaultConfigurationSection, configure);
        }

        /// <summary>
        /// Maps the OpenAPI documents, the Scalar UI and every enabled feature. <see cref="RkdScalarOptions"/>
        /// are read from <paramref name="sectionName"/> (when present) and then customized by <paramref name="configure"/>.
        /// </summary>
        public static WebApplication UseRkdScalar(
            this WebApplication app,
            string sectionName,
            Action<RkdScalarOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

            var options = new RkdScalarOptions();

            var section = app.Configuration.GetSection(sectionName);

            if (section.Exists())
                section.Bind(options);

            configure?.Invoke(options);

            var registry = app.Services.GetService<ScalarFeatureRegistry>()
                ?? throw new InvalidOperationException(
                    "Rkd.Scalar is not registered. Call builder.AddRkdScalar() before building the application.");

            registry.Options = options;

            foreach (var feature in registry.Features)
            {
                feature.ConfigureApp(app);
            }

            if (!options.Enabled)
                return app;

            app.MapOpenApi(options.OpenApiRoutePattern);

            if (options.VersionSelector)
            {
                ScalarDocumentSelector.UseVersionRedirect(app, options.ScalarRoutePrefix);

                app.MapScalarApiReference(options.ScalarRoutePrefix, (scalar, context) =>
                {
                    ApplyScalarOptions(scalar, options);
                    ScalarDocumentSelector.AddDocuments(scalar, context);
                });
            }
            else
            {
                var provider = app.Services.GetService<IApiVersionDescriptionProvider>();

                app.MapScalarApiReference(options.ScalarRoutePrefix, scalar =>
                {
                    ApplyScalarOptions(scalar, options);

                    if (provider is null)
                    {
                        scalar.AddDocument("v1");
                        return;
                    }

                    foreach (var description in provider.ApiVersionDescriptions)
                        scalar.AddDocument(description.GroupName);
                });
            }

            ReservedRouteGuard.EnsureControllersDoNotUseReservedRoutes(app);

            return app;
        }

        private static void ApplyScalarOptions(ScalarOptions scalar, RkdScalarOptions options)
        {
            scalar.Title = options.Title;
            scalar.Theme = options.Theme;
            scalar.OpenApiRoutePattern = options.OpenApiRoutePattern;

            options.ConfigureScalar?.Invoke(scalar);
        }
    }
}
