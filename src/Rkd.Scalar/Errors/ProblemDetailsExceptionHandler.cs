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

            if (status >= StatusCodes.Status500InternalServerError)
                LogServerError(_logger, exception, status, context.Request.Method, context.Request.Path);
            else
                LogClientError(_logger, status, exception.GetType().Name, context.Request.Method, context.Request.Path, exception.Message);

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

        private MvcProblemDetails CreateProblem(HttpContext context, Exception exception)
        {
            if (exception is ProblemException problemException)
                return problemException.ToProblemDetails();

            if (_options.TryMap(exception, context, out var mapped))
                return mapped;

            if (exception is BadHttpRequestException badRequest)
            {
                // Framework messages name parameters and .NET types; outside Development a safe text is sent instead.
                return new MvcProblemDetails
                {
                    Status = badRequest.StatusCode,
                    Detail = _includeDetails
                        ? badRequest.Message
                        : badRequest.StatusCode == StatusCodes.Status400BadRequest
                            ? "The request could not be read. Check the route, query string and body format."
                            : null
                };
            }

            return new MvcProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = _includeDetails ? exception.Message : null
            };
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
        private static partial void LogClientError(ILogger logger, int statusCode, string exceptionType, string method, PathString path, string message);

        [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Request {Method} {Path} was aborted by the client.")]
        private static partial void LogClientClosedRequest(ILogger logger, string method, PathString path);
    }
}
