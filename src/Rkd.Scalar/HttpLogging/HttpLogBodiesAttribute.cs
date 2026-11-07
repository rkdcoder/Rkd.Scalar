using Microsoft.AspNetCore.Builder;

namespace Rkd.Scalar
{
    /// <summary>
    /// Endpoint metadata: whether requests that succeeded keep their bodies in the HTTP log, overriding
    /// <see cref="RkdHttpLoggingOptions.LogRequestBodyOnSuccess"/> and <see cref="RkdHttpLoggingOptions.LogResponseBodyOnSuccess"/>
    /// for this endpoint. <see langword="null"/> follows the option. Failed requests always keep them, unless the endpoint
    /// is sensitive (<see cref="ISensitiveHttpLogMetadata"/>) or the capture is off.
    /// </summary>
    public interface IHttpLogBodiesMetadata
    {
        /// <summary>Whether a request that succeeded keeps its request body; <see langword="null"/> follows the option.</summary>
        bool? LogRequestBodyOnSuccess { get; }

        /// <summary>Whether a request that succeeded keeps its response body; <see langword="null"/> follows the option.</summary>
        bool? LogResponseBodyOnSuccess { get; }
    }

    /// <summary>
    /// Chooses, for a controller or action, whether requests that succeeded keep their bodies in the HTTP log. What is
    /// not set follows the options. The most specific one wins: an action overrides its controller.
    /// </summary>
    /// <example>
    /// <code>
    /// [HttpPost("chat"), HttpLogBodies(false)]                    // neither body, unless the request fails
    /// [HttpGet("{id}"), HttpLogBodies(ResponseOnSuccess = false)] // only the response body
    /// [HttpPost, HttpLogBodies(true)]                             // always, even with the options turned off
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public sealed class HttpLogBodiesAttribute : Attribute, IHttpLogBodiesMetadata
    {
        private bool? _request;

        private bool? _response;

        /// <summary>Follows the options until <see cref="RequestOnSuccess"/> or <see cref="ResponseOnSuccess"/> is set.</summary>
        public HttpLogBodiesAttribute()
        {
        }

        /// <summary>Sets both bodies.</summary>
        /// <param name="onSuccess">Whether requests that succeeded keep their request and response bodies.</param>
        public HttpLogBodiesAttribute(bool onSuccess)
        {
            _request = onSuccess;
            _response = onSuccess;
        }

        /// <summary>Whether a request that succeeded keeps its request body.</summary>
        public bool RequestOnSuccess
        {
            get => _request ?? true;
            set => _request = value;
        }

        /// <summary>Whether a request that succeeded keeps its response body.</summary>
        public bool ResponseOnSuccess
        {
            get => _response ?? true;
            set => _response = value;
        }

        bool? IHttpLogBodiesMetadata.LogRequestBodyOnSuccess => _request;

        bool? IHttpLogBodiesMetadata.LogResponseBodyOnSuccess => _response;
    }

    /// <summary>
    /// Chooses, for minimal API endpoints, whether requests that succeeded keep their bodies in the HTTP log.
    /// </summary>
    public static class HttpLogBodiesEndpointConventionBuilderExtensions
    {
        /// <summary>
        /// Requests of this endpoint (or group) that succeeded keep, or not, their request and response bodies in the HTTP
        /// log. Failed requests always keep them.
        /// </summary>
        /// <typeparam name="TBuilder">Endpoint or group builder.</typeparam>
        /// <param name="builder">The endpoint builder.</param>
        /// <param name="onSuccess">Whether requests that succeeded keep both bodies.</param>
        /// <returns>The same builder.</returns>
        /// <example><code>app.MapPost("/chat", Chat).WithHttpLogBodies(onSuccess: false);</code></example>
        public static TBuilder WithHttpLogBodies<TBuilder>(this TBuilder builder, bool onSuccess)
            where TBuilder : IEndpointConventionBuilder
        {
            ArgumentNullException.ThrowIfNull(builder);

            return builder.WithMetadata(new HttpLogBodiesAttribute(onSuccess));
        }

        /// <summary>
        /// Requests of this endpoint (or group) that succeeded keep, or not, each body in the HTTP log.
        /// <see langword="null"/> follows the options. Failed requests always keep them.
        /// </summary>
        /// <typeparam name="TBuilder">Endpoint or group builder.</typeparam>
        /// <param name="builder">The endpoint builder.</param>
        /// <param name="requestOnSuccess">Whether a request that succeeded keeps its request body.</param>
        /// <param name="responseOnSuccess">Whether a request that succeeded keeps its response body.</param>
        /// <returns>The same builder.</returns>
        /// <example><code>app.MapGet("/report", Report).WithHttpLogBodies(requestOnSuccess: null, responseOnSuccess: false);</code></example>
        public static TBuilder WithHttpLogBodies<TBuilder>(this TBuilder builder, bool? requestOnSuccess, bool? responseOnSuccess)
            where TBuilder : IEndpointConventionBuilder
        {
            ArgumentNullException.ThrowIfNull(builder);

            var metadata = new HttpLogBodiesAttribute();

            if (requestOnSuccess is { } request)
                metadata.RequestOnSuccess = request;

            if (responseOnSuccess is { } response)
                metadata.ResponseOnSuccess = response;

            return builder.WithMetadata(metadata);
        }
    }
}
