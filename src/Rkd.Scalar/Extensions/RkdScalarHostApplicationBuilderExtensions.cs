using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar;

namespace Microsoft.Extensions.Hosting
{
    /// <summary>
    /// Registers Rkd.Scalar from a host builder.
    /// </summary>
    public static class RkdScalarHostApplicationBuilderExtensions
    {
        /// <summary>
        /// Registers OpenAPI (with XML comments) and returns the Rkd.Scalar fluent builder.
        /// </summary>
        /// <example><code>builder.AddRkdScalar().WithVersioning("v1").WithBearerAuth();</code></example>
        public static RkdScalarBuilder AddRkdScalar(this IHostApplicationBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            return builder.Services.AddRkdScalar(builder.Configuration);
        }
    }
}
