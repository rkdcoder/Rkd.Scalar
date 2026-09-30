using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using MvcProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Rkd.Scalar.Errors
{
    /// <summary>
    /// Converts exceptions into problem details responses.
    /// </summary>
    /// <remarks>
    /// Resolution order: <see cref="ProblemException"/>, the mappings in <see cref="RkdProblemDetailsOptions"/>,
    /// <see cref="BadHttpRequestException"/> (malformed requests), and finally 500 for anything else.
    /// Exception messages and stack traces are only sent when <see cref="RkdProblemDetailsOptions.IncludeExceptionDetails"/>
    /// allows it (Development by default); 5xx responses never carry the message otherwise.
    /// </remarks>
    internal sealed partial class ProblemDetailsExceptionHandler
    {
        private readonly RkdProblemDetailsOptions _options;

        private readonly ProblemDetailsWriter _writer;

        private readonly ILogger<ProblemDetailsExceptionHandler> _logger;

        private readonly bool _includeDetails;

        public ProblemDetailsExceptionHandler(
            RkdProblemDetailsOptions options,
            ProblemDetailsWriter writer,
            ILogger<ProblemDetailsExceptionHandler> logger,
            IHostEnvironment environment)
        {
            _options = options;
            _writer = writer;
            _logger = logger;
            _includeDetails = options.IncludeExceptionDetails ?? environment.IsDevelopment();
        }

        public async Task HandleAsync(HttpContext context, Exception exception)
        {
            exception = Unwrap(exception);

            ResetResponse(context);

            if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            {
                // The client is gone: nothing to write and nothing worth an error log.
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
                LogClientClosedRequest(_logger, context.Request.Method, context.Request.Path);
                return;
            }

            var problem = CreateProblem(context, exception);
            var status = problem.Status!.Value;

            Log(context, exception, status);

            // The HTTP log is outside this middleware and would not see the exception otherwise.
            if (status >= StatusCodes.Status500InternalServerError)
                HttpLogging.HttpLogItems.SetException(context, exception);

            if (_includeDetails)
            {
                problem.Extensions["exception"] = new Dictionary<string, object?>
                {
                    ["type"] = exception.GetType().FullName,
                    ["message"] = exception.Message,
                    ["stackTrace"] = exception.ToString()
                };
            }

            await _writer.WriteAsync(context, problem, exception);
        }

        private void Log(HttpContext context, Exception exception, int status)
        {
            var request = context.Request;
            var logDetails = exception is Rkd.Problems.HttpProblemException { Problem: { } upstream } && _options.MapsUpstreamProblems
                ? UpstreamLogDetails(upstream)
                : (exception as IProblemLogDetails)?.LogDetails;

            if (status >= StatusCodes.Status500InternalServerError)
            {
                if (string.IsNullOrEmpty(logDetails))
                    LogServerError(_logger, exception, status, request.Method, request.Path);
                else
                    LogServerErrorWithDetails(_logger, exception, status, request.Method, request.Path, logDetails);

                return;
            }

            // 4xx are expected: no stack trace, unless there is an inner exception (the real cause) to log.
            var cause = exception.InnerException is null ? null : exception;

            if (string.IsNullOrEmpty(logDetails))
                LogClientError(_logger, cause, status, exception.GetType().Name, request.Method, request.Path, exception.Message);
            else
                LogClientErrorWithDetails(_logger, cause, status, exception.GetType().Name, request.Method, request.Path, exception.Message, logDetails);
        }

        private MvcProblemDetails CreateProblem(HttpContext context, Exception exception)
        {
            if (exception is ProblemException problemException)
                return problemException.ToProblemDetails();

            if (_options.MapsUpstreamProblems && exception is Rkd.Problems.HttpProblemException upstream)
                return FromUpstream(upstream.Problem);

            if (_options.TryMap(exception, context, out var mapped))
                return mapped;

            if (IsRequestBodyError(exception, out var bodyStatus))
            {
                // Form/multipart limits (MultipartBodyLengthLimit, ValueCountLimit…) and malformed forms: client errors.
                return new MvcProblemDetails
                {
                    Status = bodyStatus,
                    Detail = _includeDetails ? exception.Message : SafeDetail(bodyStatus)
                };
            }

            if (exception is BadHttpRequestException badRequest)
            {
                // Framework messages name parameters and .NET types; outside Development a safe text is sent instead.
                return new MvcProblemDetails
                {
                    Status = badRequest.StatusCode,
                    Detail = _includeDetails ? badRequest.Message : SafeDetail(badRequest.StatusCode)
                };
            }

            var unexpected = new MvcProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = _options.UnexpectedErrorTitle ?? "An unexpected error occurred.",
                Detail = _options.UnexpectedErrorDetail ?? (_includeDetails ? exception.Message : null)
            };

            if (_options.UnexpectedErrorCode is { } code)
                unexpected.Extensions[ProblemCodes.ExtensionName] = code;

            return unexpected;
        }

        /// <summary>4xx of the other API keep its status, code, title, detail and errors; 5xx become 502.</summary>
        private static MvcProblemDetails FromUpstream(Rkd.Problems.HttpProblem upstream)
        {
            if (upstream.Status is < 400 or > 499)
                return new MvcProblemDetails { Status = StatusCodes.Status502BadGateway };

            var problem = new MvcProblemDetails
            {
                Status = upstream.Status,
                Title = upstream.Title,
                Detail = upstream.Detail,
                Extensions = { [ProblemCodes.ExtensionName] = upstream.Code }
            };

            if (upstream.Errors is { Count: > 0 } errors)
                problem.Extensions[ProblemCodes.ErrorsName] = errors;

            return problem;
        }

        /// <summary>What the log keeps of a failed call to another API, to correlate both logs.</summary>
        private static string UpstreamLogDetails(Rkd.Problems.HttpProblem upstream) =>
            $"Upstream problem: status={upstream.Status} code={upstream.Code} traceId={upstream.TraceId ?? "-"} " +
            $"instance={upstream.Instance ?? "-"} detail={upstream.Detail ?? "-"}";

        private static string? SafeDetail(int status) => status switch
        {
            StatusCodes.Status400BadRequest => "The request could not be read. Check the route, query string and body format.",
            StatusCodes.Status413PayloadTooLarge => "The request body is larger than the maximum allowed size.",
            _ => null
        };

        /// <summary>
        /// <see cref="InvalidDataException"/> thrown by ASP.NET Core while reading a form or a multipart body
        /// (possibly wrapped by MVC). Exceeded limits become 413, malformed bodies 400. The same exception type
        /// thrown by your own code is not affected.
        /// </summary>
        private static bool IsRequestBodyError(Exception exception, out int status)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is InvalidDataException &&
                    current.Source is "Microsoft.AspNetCore.WebUtilities" or "Microsoft.AspNetCore.Http")
                {
                    status = current.Message.Contains("limit", StringComparison.OrdinalIgnoreCase)
                        ? StatusCodes.Status413PayloadTooLarge
                        : StatusCodes.Status400BadRequest;
                    return true;
                }
            }

            status = 0;
            return false;
        }

        private static Exception Unwrap(Exception exception) =>
            exception is AggregateException { InnerExceptions.Count: 1 } aggregate
                ? Unwrap(aggregate.InnerExceptions[0])
                : exception;

        /// <summary>
        /// Discards whatever the failed request wrote to the response (status, headers, buffered body)
        /// and prevents caches from storing the error. <c>OnStarting</c> callbacks (e.g. CORS) are kept.
        /// </summary>
        private static void ResetResponse(HttpContext context)
        {
            context.Response.Clear();

            var headers = context.Response.Headers;
            headers[HeaderNames.CacheControl] = "no-cache,no-store";
            headers[HeaderNames.Pragma] = "no-cache";
            headers[HeaderNames.Expires] = "-1";
        }

        [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Unhandled exception returned as {StatusCode} for {Method} {Path}.")]
        private static partial void LogServerError(ILogger logger, Exception exception, int statusCode, string method, PathString path);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "{ExceptionType} returned as {StatusCode} for {Method} {Path}: {Message}")]
        private static partial void LogClientError(ILogger logger, Exception? exception, int statusCode, string exceptionType, string method, PathString path, string message);

        [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "{ExceptionType} returned as {StatusCode} for {Method} {Path}: {Message} Details: {LogDetails}")]
        private static partial void LogClientErrorWithDetails(ILogger logger, Exception? exception, int statusCode, string exceptionType, string method, PathString path, string message, string logDetails);

        [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Unhandled exception returned as {StatusCode} for {Method} {Path}. Details: {LogDetails}")]
        private static partial void LogServerErrorWithDetails(ILogger logger, Exception exception, int statusCode, string method, PathString path, string logDetails);

        [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Request {Method} {Path} was aborted by the client.")]
        private static partial void LogClientClosedRequest(ILogger logger, string method, PathString path);
    }
}
