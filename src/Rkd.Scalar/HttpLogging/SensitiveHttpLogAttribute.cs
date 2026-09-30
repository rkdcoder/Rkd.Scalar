using Microsoft.AspNetCore.Builder;

namespace Rkd.Scalar
{
    /// <summary>
    /// Endpoint metadata: the HTTP log keeps the entry but never stores the request and response bodies
    /// (<c>[REDACTED]</c>), like <see cref="RkdHttpLoggingOptions.SensitivePaths"/> — for routes a path prefix cannot
    /// describe, e.g. <c>POST api/v1/systems/{id}/keys</c>.
    /// </summary>
    public interface ISensitiveHttpLogMetadata
    {
    }

    /// <summary>
    /// Marks a controller or action whose request and response bodies must never be written to the HTTP log.
    /// </summary>
    /// <example>
    /// <code>
    /// [HttpPost("{id}/keys"), SensitiveHttpLog]
    /// public ApiKeyCreated CreateKey(int id) => ...;   // the response carries the key in clear text
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public sealed class SensitiveHttpLogAttribute : Attribute, ISensitiveHttpLogMetadata
    {
    }

    /// <summary>
    /// Marks minimal API endpoints whose bodies must never be written to the HTTP log.
    /// </summary>
    public static class SensitiveHttpLogEndpointConventionBuilderExtensions
    {
        /// <summary>
        /// The HTTP log keeps the entry of this endpoint (or group) without its request and response bodies.
        /// </summary>
        /// <typeparam name="TBuilder">Endpoint or group builder.</typeparam>
        /// <param name="builder">The endpoint builder.</param>
        /// <returns>The same builder.</returns>
        /// <example><code>app.MapPost("/systems/{id}/keys", CreateKey).WithSensitiveHttpLog();</code></example>
        public static TBuilder WithSensitiveHttpLog<TBuilder>(this TBuilder builder)
            where TBuilder : IEndpointConventionBuilder
        {
            ArgumentNullException.ThrowIfNull(builder);

            return builder.WithMetadata(new SensitiveHttpLogAttribute());
        }
    }
}
