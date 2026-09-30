namespace Rkd.Scalar
{
    /// <summary>
    /// One HTTP request captured by <c>WithHttpLogging()</c>, delivered to every <see cref="IHttpLogSink"/>.
    /// Sensitive data is already redacted (credentials in headers and query strings, bodies of sensitive paths).
    /// </summary>
    public sealed class HttpLogEntry
    {
        /// <summary>
        /// Trace identifier — the same <c>traceId</c> of the problem details responses and of the application logs.
        /// </summary>
        public string TraceId { get; set; } = string.Empty;

        /// <summary>When the request started (UTC).</summary>
        public DateTimeOffset StartedAt { get; set; }

        /// <summary>Time spent until the response was produced.</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>Application name (<see cref="RkdHttpLoggingOptions.ApplicationName"/>).</summary>
        public string? ApplicationName { get; set; }

        /// <summary>HTTP method.</summary>
        public string Method { get; set; } = string.Empty;

        /// <summary><c>http</c> or <c>https</c>.</summary>
        public string Scheme { get; set; } = string.Empty;

        /// <summary>Host header (with port).</summary>
        public string? Host { get; set; }

        /// <summary>Path base (virtual directory), when hosted under one.</summary>
        public string? PathBase { get; set; }

        /// <summary>Request path.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>Query string, with the values of <see cref="RkdHttpLoggingOptions.RedactedQueryParameters"/> redacted.</summary>
        public string? QueryString { get; set; }

        /// <summary>Route template of the matched endpoint (e.g. <c>api/v{version}/orders/{id}</c>), useful to group requests.</summary>
        public string? RoutePattern { get; set; }

        /// <summary>HTTP protocol (<c>HTTP/1.1</c>, <c>HTTP/2</c>…).</summary>
        public string? Protocol { get; set; }

        /// <summary>Response status code.</summary>
        public int StatusCode { get; set; }

        /// <summary>Whether <see cref="StatusCode"/> is below 400.</summary>
        public bool IsSuccess { get; set; }

        /// <summary>The <c>code</c> of the problem details response (e.g. <c>CUSTOMER_NOT_FOUND</c>), when there was one.</summary>
        public string? ErrorCode { get; set; }

        /// <summary>The unhandled exception (type, message and stack trace), when the request failed with one.</summary>
        public string? Exception { get; set; }

        /// <summary><c>Identity.Name</c> of the authenticated user.</summary>
        public string? UserName { get; set; }

        /// <summary><c>NameIdentifier</c> (<c>sub</c>) of the authenticated user.</summary>
        public string? UserId { get; set; }

        /// <summary>Client IP address (after <c>ForwardedHeaders</c>, when configured).</summary>
        public string? ClientIp { get; set; }

        /// <summary>Client port.</summary>
        public int? ClientPort { get; set; }

        /// <summary>Server IP address.</summary>
        public string? LocalIp { get; set; }

        /// <summary>Server port.</summary>
        public int? LocalPort { get; set; }

        /// <summary>Connection identifier.</summary>
        public string? ConnectionId { get; set; }

        /// <summary><c>User-Agent</c> header.</summary>
        public string? UserAgent { get; set; }

        /// <summary><c>Referer</c> header.</summary>
        public string? Referer { get; set; }

        /// <summary><c>Accept-Language</c> header.</summary>
        public string? Locale { get; set; }

        /// <summary>Request headers, with the values of <see cref="RkdHttpLoggingOptions.RedactedHeaders"/> redacted.</summary>
        public IDictionary<string, string> RequestHeaders { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Request <c>Content-Type</c>.</summary>
        public string? RequestContentType { get; set; }

        /// <summary>Request <c>Content-Length</c>.</summary>
        public long? RequestContentLength { get; set; }

        /// <summary>
        /// Request body read by the application (text content types only, up to <see cref="RkdHttpLoggingOptions.MaxBodyBytes"/>);
        /// <c>[REDACTED]</c> on sensitive paths.
        /// </summary>
        public string? RequestBody { get; set; }

        /// <summary>Whether <see cref="RequestBody"/> was cut at <see cref="RkdHttpLoggingOptions.MaxBodyBytes"/>.</summary>
        public bool RequestBodyTruncated { get; set; }

        /// <summary>Response <c>Content-Type</c>.</summary>
        public string? ResponseContentType { get; set; }

        /// <summary>Bytes written to the response body.</summary>
        public long? ResponseSize { get; set; }

        /// <summary>
        /// Response body (text content types only, up to <see cref="RkdHttpLoggingOptions.MaxBodyBytes"/>);
        /// <c>[REDACTED]</c> on sensitive paths.
        /// </summary>
        public string? ResponseBody { get; set; }

        /// <summary>Whether <see cref="ResponseBody"/> was cut at <see cref="RkdHttpLoggingOptions.MaxBodyBytes"/>.</summary>
        public bool ResponseBodyTruncated { get; set; }

        /// <summary>Custom values added by <see cref="RkdHttpLoggingOptions.Enrich"/> (tenant, correlation ids…).</summary>
        public IDictionary<string, object?> Properties { get; set; } = new Dictionary<string, object?>(StringComparer.Ordinal);
    }
}
