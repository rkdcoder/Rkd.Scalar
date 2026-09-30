using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Rkd.Scalar;
using Rkd.Scalar.Infrastructure;
using Rkd.Scalar.OpenApi.XmlComments;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers Rkd.Scalar in an <see cref="IServiceCollection"/>.
    /// </summary>
    public static class RkdScalarServiceCollectionExtensions
    {
        /// <summary>
        /// Registers OpenAPI (with XML comments) and returns the Rkd.Scalar fluent builder.
        /// </summary>
        /// <param name="services">The application services.</param>
        /// <param name="configuration">The application configuration, used by the section-based features.</param>
        public static RkdScalarBuilder AddRkdScalar(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

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

            return new RkdScalarBuilder(services, configuration, registry);
        }
    }
}
