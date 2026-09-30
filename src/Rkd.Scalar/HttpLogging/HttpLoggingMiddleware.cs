using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rkd.Scalar.Infrastructure;

namespace Rkd.Scalar.HttpLogging
{
    /// <summary>
    /// Outermost middleware of <c>WithHttpLogging()</c>: measures the request, captures what the application reads
    /// and writes (without extra buffering), redacts credentials and queues the entry. Nothing here can fail or
    /// slow down the request: capture errors are ignored and the queue never waits.
    /// </summary>
    internal sealed partial class HttpLoggingMiddleware
    {
        private const string Redacted = "[REDACTED]";

        private const int MaxExceptionLength = 32 * 1024;

        private readonly RequestDelegate _next;

        private readonly RkdHttpLoggingOptions _options;

        private readonly HttpLogQueue _queue;

        private readonly ScalarFeatureRegistry _registry;

        private readonly ILogger<HttpLoggingMiddleware> _logger;

        private readonly string _applicationName;

        private readonly HashSet<string> _redactedHeaders;

        public HttpLoggingMiddleware(
            RequestDelegate next,
            RkdHttpLoggingOptions options,
            HttpLogQueue queue,
            ScalarFeatureRegistry registry,
            IHostEnvironment environment,
            IOptionsMonitor<ApiKeyAuthenticationOptions> apiKeyOptions,
            ILogger<HttpLoggingMiddleware> logger)
        {
            _next = next;
            _options = options;
            _queue = queue;
            _registry = registry;
            _logger = logger;
            _applicationName = options.ApplicationName ?? environment.ApplicationName;
            _redactedHeaders = new HashSet<string>(options.RedactedHeaders, StringComparer.OrdinalIgnoreCase);

            // The header of WithApiKeyAuth carries a credential too, whatever its name.
            if (registry.AuthenticationSchemes.Contains(RkdScalarAuthenticationSchemes.ApiKey) &&
                apiKeyOptions.Get(RkdScalarAuthenticationSchemes.ApiKey).HeaderName is { Length: > 0 } apiKeyHeader)
            {
                _redactedHeaders.Add(apiKeyHeader);
            }
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_options.Enabled || IsExcluded(context.Request.Path))
            {
                await _next(context);
                return;
            }

            var startedAt = DateTimeOffset.UtcNow;
            var timestamp = Stopwatch.GetTimestamp();
            var sensitive = MatchesAny(context.Request.Path, _options.SensitivePaths) ||
                            MatchesAny(context.Request.Path, _registry.SensitivePaths);

            CapturingRequestStream? requestCapture = null;
            Stream? originalRequestBody = null;

            if (_options.CaptureRequestBody && !sensitive && IsText(context.Request.ContentType))
            {
                originalRequestBody = context.Request.Body;
                requestCapture = new CapturingRequestStream(originalRequestBody, _options.MaxBodyBytes);
                context.Request.Body = requestCapture;
            }

            var originalResponseBody = context.Features.Get<IHttpResponseBodyFeature>();
            CapturingResponseBody? responseCapture = null;

            if (originalResponseBody is not null)
            {
                var captureText = _options.CaptureResponseBody && !sensitive;

                responseCapture = new CapturingResponseBody(
                    originalResponseBody,
                    captureText ? _options.MaxBodyBytes : 0,
                    () => captureText && IsText(context.Response.ContentType));

                context.Features.Set<IHttpResponseBodyFeature>(responseCapture);
            }

            Exception? unhandled = null;

            try
            {
                await _next(context);
            }
            catch (Exception exception)
            {
                unhandled = exception;
                throw;
            }
            finally
            {
                if (responseCapture is not null)
                {
                    try
                    {
                        await responseCapture.FlushWriterAsync();
                    }
                    finally
                    {
                        context.Features.Set(originalResponseBody);
                    }
                }

                if (originalRequestBody is not null)
                    context.Request.Body = originalRequestBody;

                try
                {
                    var entry = CreateEntry(context, startedAt, timestamp, sensitive, requestCapture, responseCapture, unhandled);

                    if (entry is not null)
                        _queue.Enqueue(entry);
                }
                catch (Exception exception)
                {
                    LogCaptureFailed(_logger, exception, context.Request.Path);
                }
            }
        }

        private HttpLogEntry? CreateEntry(
            HttpContext context,
            DateTimeOffset startedAt,
            long timestamp,
            bool sensitive,
            CapturingRequestStream? requestCapture,
            CapturingResponseBody? responseCapture,
            Exception? unhandled)
        {
            var request = context.Request;
            var response = context.Response;

            // An exception that escapes the pipeline becomes a 500 for the client.
            var statusCode = unhandled is not null && !response.HasStarted ? StatusCodes.Status500InternalServerError : response.StatusCode;

            if (_options.Filter is not null && !_options.Filter(context))
                return null;

            var exception = unhandled ?? HttpLogItems.GetException(context);
            var connection = context.Connection;

            var entry = new HttpLogEntry
            {
                TraceId = Activity.Current?.Id ?? context.TraceIdentifier,
                StartedAt = startedAt,
                Duration = Stopwatch.GetElapsedTime(timestamp),
                ApplicationName = _applicationName,
                Method = request.Method,
                Scheme = request.Scheme,
                Host = request.Host.HasValue ? request.Host.Value : null,
                PathBase = request.PathBase.HasValue ? request.PathBase.Value : null,
                Path = request.Path.Value ?? string.Empty,
                QueryString = RedactQuery(request),
                RoutePattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText,
                Protocol = request.Protocol,
                StatusCode = statusCode,
                IsSuccess = statusCode < 400,
                ErrorCode = HttpLogItems.GetErrorCode(context),
                Exception = exception is null ? null : Truncate(exception.ToString(), MaxExceptionLength),
                UserName = FindClaim(context.User, _options.UserNameClaimTypes),
                UserId = FindClaim(context.User, _options.UserIdClaimTypes),
                ClientIp = connection.RemoteIpAddress?.ToString(),
                ClientPort = connection.RemotePort == 0 ? null : connection.RemotePort,
                LocalIp = connection.LocalIpAddress?.ToString(),
                LocalPort = connection.LocalPort == 0 ? null : connection.LocalPort,
                ConnectionId = connection.Id,
                UserAgent = request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? agent : null,
                Referer = request.Headers.Referer.ToString() is { Length: > 0 } referer ? referer : null,
                Locale = request.Headers.AcceptLanguage.ToString() is { Length: > 0 } locale ? locale : null,
                RequestContentType = request.ContentType,
                RequestContentLength = request.ContentLength,
                ResponseContentType = response.ContentType,
                ResponseSize = responseCapture?.Size
            };

            if (_options.CaptureRequestHeaders)
            {
                foreach (var (name, value) in request.Headers)
                    entry.RequestHeaders[name] = RedactHeader(name, value.ToString());
            }

            if (sensitive)
            {
                entry.RequestBody = _options.CaptureRequestBody && IsText(request.ContentType) ? Redacted : null;
                entry.ResponseBody = _options.CaptureResponseBody && IsText(response.ContentType) ? Redacted : null;
            }
            else
            {
                if (requestCapture is not null)
                {
                    entry.RequestBody = requestCapture.Capture.GetTextAndRelease();
                    entry.RequestBodyTruncated = requestCapture.Capture.Truncated;
                }

                if (responseCapture is { Captured: true })
                {
                    entry.ResponseBody = responseCapture.Capture.GetTextAndRelease();
                    entry.ResponseBodyTruncated = responseCapture.Capture.Truncated;
                }
            }

            _options.Enrich?.Invoke(context, entry);

            return entry;
        }

        private bool IsExcluded(PathString path)
        {
            if (MatchesAny(path, _options.ExcludedPaths))
                return true;

            if (!_options.ExcludeDocumentation)
                return false;

            var options = _registry.Options;
            var openApiPrefix = options.OpenApiRoutePattern.Split('{')[0].TrimEnd('/');

            return path.StartsWithSegments(options.ScalarRoutePrefix, StringComparison.OrdinalIgnoreCase) ||
                   (openApiPrefix.Length > 0 && path.StartsWithSegments(openApiPrefix, StringComparison.OrdinalIgnoreCase));
        }

        private static bool MatchesAny(PathString path, IEnumerable<string> prefixes)
        {
            foreach (var prefix in prefixes)
            {
                if (!string.IsNullOrWhiteSpace(prefix) &&
                    path.StartsWithSegments("/" + prefix.Trim().Trim('/'), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string RedactHeader(string name, string value)
        {
            if (!_redactedHeaders.Contains(name))
                return value;

            // Authorization keeps its scheme ("Bearer [REDACTED]"), useful to tell the authentication used.
            if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase))
            {
                var space = value.IndexOf(' ');
                return space > 0 ? $"{value[..space]} {Redacted}" : Redacted;
            }

            return Redacted;
        }

        private string? RedactQuery(HttpRequest request)
        {
            if (!request.QueryString.HasValue)
                return null;

            if (!request.Query.Keys.Any(_options.RedactedQueryParameters.Contains))
                return request.QueryString.Value;

            var builder = new StringBuilder("?");

            foreach (var (key, values) in request.Query)
            {
                var redact = _options.RedactedQueryParameters.Contains(key);

                foreach (var value in values.Count == 0 ? [string.Empty] : values.ToArray())
                {
                    if (builder.Length > 1)
                        builder.Append('&');

                    builder.Append(Uri.EscapeDataString(key)).Append('=')
                        .Append(redact ? Redacted : Uri.EscapeDataString(value ?? string.Empty));
                }
            }

            return builder.ToString();
        }

        private static string? FindClaim(ClaimsPrincipal? user, string[]? claimTypes)
        {
            if (user?.Identity?.IsAuthenticated != true || claimTypes is null)
                return null;

            foreach (var type in claimTypes)
            {
                if (!string.IsNullOrWhiteSpace(type) && user.FindFirst(type)?.Value is { Length: > 0 } value)
                    return value;
            }

            return null;
        }

        /// <summary>Text content types: JSON, XML, text/*, forms and JavaScript. Multipart (files) and binaries are not captured.</summary>
        internal static bool IsText(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                return false;

            var mediaType = contentType.Split(';')[0].Trim();

            return mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                   mediaType.EndsWith("json", StringComparison.OrdinalIgnoreCase) ||
                   mediaType.EndsWith("xml", StringComparison.OrdinalIgnoreCase) ||
                   mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) ||
                   mediaType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase) ||
                   mediaType.Equals("application/graphql", StringComparison.OrdinalIgnoreCase);
        }

        private static string Truncate(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength];

        [LoggerMessage(EventId = 34, Level = LogLevel.Debug, Message = "The HTTP log entry of {Path} could not be created.")]
        private static partial void LogCaptureFailed(ILogger logger, Exception exception, PathString path);
    }
}
