using Microsoft.AspNetCore.Http;

namespace Rkd.Scalar
{
    /// <summary>
    /// Options of <c>WithHttpLogging()</c>. Bound from the <c>HttpLogging</c> configuration section when it exists,
    /// then changed by the code passed to <c>WithHttpLogging</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// "HttpLogging": {
    ///   "MaxBodyBytes": 32768,
    ///   "ExcludedPaths": [ "/health" ],
    ///   "SensitivePaths": [ "/api/v1/users/password" ]
    /// }
    /// </code>
    /// </example>
    public sealed class RkdHttpLoggingOptions
    {
        /// <summary>Turns the capture on or off (e.g. per environment). Defaults to <see langword="true"/>.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Maximum number of entries waiting to be written. When the queue is full (sinks slower than the traffic),
        /// new entries are dropped — requests are never blocked. Defaults to 10,000.
        /// </summary>
        public int QueueCapacity { get; set; } = 10_000;

        /// <summary>Maximum number of entries passed to a sink at once. Defaults to 100.</summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>How long the queued entries may take to be written when the application stops. Defaults to 5 seconds.</summary>
        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Maximum bytes kept of each request and response body. Defaults to 32 KB.</summary>
        public int MaxBodyBytes { get; set; } = 32 * 1024;

        /// <summary>Captures request bodies (text content types only). Defaults to <see langword="true"/>.</summary>
        public bool CaptureRequestBody { get; set; } = true;

        /// <summary>Captures response bodies (text content types only). Defaults to <see langword="true"/>.</summary>
        public bool CaptureResponseBody { get; set; } = true;

        /// <summary>Captures request headers. Defaults to <see langword="true"/>.</summary>
        public bool CaptureRequestHeaders { get; set; } = true;

        /// <summary>
        /// Does not log the Scalar UI and the OpenAPI documents. Defaults to <see langword="true"/>.
        /// </summary>
        public bool ExcludeDocumentation { get; set; } = true;

        /// <summary>
        /// Paths that are not logged (by segment: <c>/health</c> also excludes <c>/health/ready</c>, not <c>/healthy</c>).
        /// </summary>
        public ISet<string> ExcludedPaths { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Paths whose request and response bodies are replaced by <c>[REDACTED]</c> (logins, password changes, tokens).
        /// The login endpoint of <c>WithJwtLoginEndpoint</c> is always included.
        /// </summary>
        public ISet<string> SensitivePaths { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Request headers whose values are replaced by <c>[REDACTED]</c>. <c>Authorization</c> keeps only its scheme
        /// (<c>Bearer [REDACTED]</c>). Defaults: <c>Authorization</c>, <c>Proxy-Authorization</c>, <c>Cookie</c>,
        /// <c>X-API-Key</c> (and the header configured in <c>WithApiKeyAuth</c>).
        /// </summary>
        public ISet<string> RedactedHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization", "Proxy-Authorization", "Cookie", "X-API-Key"
        };

        /// <summary>
        /// Query string parameters whose values are replaced by <c>[REDACTED]</c>. Defaults: <c>access_token</c>,
        /// <c>token</c>, <c>api_key</c>, <c>apikey</c>, <c>password</c>, <c>secret</c>.
        /// </summary>
        public ISet<string> RedactedQueryParameters { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "access_token", "token", "api_key", "apikey", "password", "secret"
        };

        /// <summary>
        /// Claims read, in order, to fill <see cref="HttpLogEntry.UserName"/> — the first one present wins. Replace
        /// them when users are identified by something else (e.g. <c>["cpf"]</c> or <c>["email"]</c>).
        /// Defaults: <c>ClaimTypes.Name</c>, <c>name</c>, <c>preferred_username</c>, <c>unique_name</c>,
        /// <c>ClaimTypes.Email</c>, <c>email</c>.
        /// </summary>
        public string[] UserNameClaimTypes { get; set; } =
        [
            System.Security.Claims.ClaimTypes.Name, "name", "preferred_username", "unique_name",
            System.Security.Claims.ClaimTypes.Email, "email"
        ];

        /// <summary>
        /// Claims read, in order, to fill <see cref="HttpLogEntry.UserId"/>. Defaults: <c>ClaimTypes.NameIdentifier</c>,
        /// <c>sub</c>, <c>oid</c>.
        /// </summary>
        public string[] UserIdClaimTypes { get; set; } =
        [
            System.Security.Claims.ClaimTypes.NameIdentifier, "sub", "oid"
        ];

        /// <summary>Application name written to every entry. Defaults to the host application name.</summary>
        public string? ApplicationName { get; set; }

        /// <summary>
        /// Decides, after the response, whether the request is logged (e.g. only errors:
        /// <c>context =&gt; context.Response.StatusCode &gt;= 400</c>).
        /// </summary>
        public Func<HttpContext, bool>? Filter { get; set; }

        /// <summary>Adds or changes data of the entry before it is queued (e.g. <c>entry.Properties["tenant"] = ...</c>).</summary>
        public Action<HttpContext, HttpLogEntry>? Enrich { get; set; }

        internal void Validate()
        {
            if (QueueCapacity <= 0)
                throw new InvalidOperationException("HttpLogging: 'QueueCapacity' must be greater than zero.");

            if (BatchSize <= 0)
                throw new InvalidOperationException("HttpLogging: 'BatchSize' must be greater than zero.");

            if (MaxBodyBytes < 0)
                throw new InvalidOperationException("HttpLogging: 'MaxBodyBytes' cannot be negative.");

            if (ShutdownTimeout < TimeSpan.Zero)
                throw new InvalidOperationException("HttpLogging: 'ShutdownTimeout' cannot be negative.");
        }
    }
}
