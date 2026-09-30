using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.Logging;
using MvcProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Rkd.Scalar.Errors
{
    /// <summary>
    /// Outermost middleware (registered through an <see cref="Microsoft.AspNetCore.Hosting.IStartupFilter"/>) that
    /// turns unhandled exceptions and body-less error responses into RFC 9457 problem details.
    /// </summary>
    internal sealed partial class ProblemDetailsMiddleware
    {
        private readonly RequestDelegate _next;

        private readonly RkdProblemDetailsOptions _options;

        private readonly ProblemDetailsExceptionHandler _handler;

        private readonly ProblemDetailsWriter _writer;

        private readonly ILogger<ProblemDetailsMiddleware> _logger;

        public ProblemDetailsMiddleware(
            RequestDelegate next,
            RkdProblemDetailsOptions options,
            ProblemDetailsExceptionHandler handler,
            ProblemDetailsWriter writer,
            ILogger<ProblemDetailsMiddleware> logger)
        {
            _next = next;
            _options = options;
            _handler = handler;
            _writer = writer;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Exposes the standard feature so endpoints and middleware can opt out
            // ([SkipStatusCodePages] or IStatusCodePagesFeature.Enabled = false).
            var statusCodePages = new StatusCodePagesFeature { Enabled = _options.HandleStatusCodes };
            context.Features.Set<IStatusCodePagesFeature>(statusCodePages);

            try
            {
                await _next(context);
            }
            catch (Exception exception)
            {
                if (context.Response.HasStarted)
                {
                    // Headers are already sent: the response cannot be replaced, only aborted.
                    LogResponseStarted(_logger, exception, context.Request.Method, context.Request.Path);
                    throw;
                }

                try
                {
                    await _handler.HandleAsync(context, exception);
                }
                catch (Exception writeException)
                {
                    LogWriteFailed(_logger, writeException, context.Request.Method, context.Request.Path);
                    throw new AggregateException(exception, writeException);
                }

                return;
            }

            if (ShouldWriteStatusCodeProblem(context, statusCodePages))
                await _writer.WriteAsync(context, new MvcProblemDetails { Status = context.Response.StatusCode });
        }

        private static bool ShouldWriteStatusCodeProblem(HttpContext context, StatusCodePagesFeature feature)
        {
            var response = context.Response;

            return feature.Enabled &&
                   !response.HasStarted &&
                   response.StatusCode is >= 400 and <= 599 &&
                   response.ContentLength is null &&
                   string.IsNullOrEmpty(response.ContentType) &&
                   !HttpMethods.IsHead(context.Request.Method) &&
                   context.GetEndpoint()?.Metadata.GetMetadata<ISkipStatusCodePagesMetadata>() is null;
        }

        private sealed class StatusCodePagesFeature : IStatusCodePagesFeature
        {
            public bool Enabled { get; set; }
        }

        [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "Unhandled exception after the response started for {Method} {Path}; the response cannot be replaced.")]
        private static partial void LogResponseStarted(ILogger logger, Exception exception, string method, PathString path);

        [LoggerMessage(EventId = 11, Level = LogLevel.Error, Message = "Failed to write the problem details response for {Method} {Path}.")]
        private static partial void LogWriteFailed(ILogger logger, Exception exception, string method, PathString path);
    }
}
