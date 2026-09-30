using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using MvcProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Rkd.Scalar
{
    /// <summary>
    /// Options of the standardized error responses (RFC 9457 <c>application/problem+json</c>)
    /// enabled by <c>WithProblemDetails()</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.AddRkdScalar().WithProblemDetails(options =>
    /// {
    ///     options.Map&lt;NotFoundException&gt;(StatusCodes.Status404NotFound);
    ///     options.Map&lt;ConflictException&gt;(StatusCodes.Status409Conflict, "Conflict");
    ///     options.Map&lt;ValidationException&gt;(ex => new ProblemDetails { Status = 422, Detail = ex.Message });
    /// });
    /// </code>
    /// </example>
    public sealed class RkdProblemDetailsOptions
    {
        private readonly Dictionary<Type, Func<Exception, HttpContext, MvcProblemDetails>> _mappings = new();

        /// <summary>
        /// Adds the exception type, message and stack trace to the response (<c>exception</c> extension).
        /// <see langword="null"/> (default) enables it only in the Development environment.
        /// Never enable it in production: it leaks implementation details.
        /// </summary>
        public bool? IncludeExceptionDetails { get; set; }

        /// <summary>
        /// Writes a problem details body for error responses (400–599) produced without a body, such as
        /// 404 for unknown routes, 405, 415, or 401/403 from authorization. Defaults to <see langword="true"/>.
        /// Endpoints can opt out with <c>[SkipStatusCodePages]</c>.
        /// </summary>
        public bool HandleStatusCodes { get; set; } = true;

        /// <summary>
        /// Sets <c>instance</c> to the request path when it is empty. Defaults to <see langword="true"/>.
        /// </summary>
        public bool IncludeInstance { get; set; } = true;

        /// <summary>
        /// Adds a machine-readable <c>code</c> to every problem that has none: <c>VALIDATION_ERROR</c> for validation
        /// problems and the status reason phrase otherwise (<c>NOT_FOUND</c>, <c>UNAUTHORIZED</c>,
        /// <c>INTERNAL_SERVER_ERROR</c>…). Explicit codes (<see cref="RkdError"/>, <see cref="ProblemException.Code"/>)
        /// always win. Defaults to <see langword="true"/>.
        /// </summary>
        public bool IncludeDefaultCodes { get; set; } = true;

        /// <summary>
        /// Documents the problem responses in OpenAPI / Scalar: 400 for operations with parameters or a body,
        /// 401 and 403 for operations that require authorization, 429 for rate limited operations and 500 for every
        /// operation; error responses you declare (e.g. <c>[ProducesResponseType(404)]</c>) get the problem schema.
        /// Defaults to <see langword="true"/>.
        /// </summary>
        public bool DocumentErrorResponses { get; set; } = true;

        /// <summary>
        /// Customizes every problem details response (exceptions, status codes, <c>Results.Problem</c>,
        /// <c>ValidationProblem</c>…), e.g. to add extensions such as a tenant or an error catalog link.
        /// </summary>
        public Action<ProblemDetailsContext>? Customize { get; set; }

        /// <summary>
        /// Maps <typeparamref name="TException"/> (and derived types) to <paramref name="statusCode"/>.
        /// </summary>
        /// <typeparam name="TException">Exception type.</typeparam>
        /// <param name="statusCode">HTTP status code of the response.</param>
        /// <param name="title">Title of the problem. Defaults to the status code reason phrase.</param>
        /// <param name="exposeMessage">
        /// Whether <see cref="Exception.Message"/> becomes the <c>detail</c>. Defaults to <see langword="true"/> for
        /// 4xx (your domain messages) and <see langword="false"/> for 5xx (never leak server errors).
        /// </param>
        /// <returns>The same options, for chaining.</returns>
        public RkdProblemDetailsOptions Map<TException>(int statusCode, string? title = null, bool? exposeMessage = null)
            where TException : Exception
        {
            if (statusCode is < 400 or > 599)
                throw new ArgumentOutOfRangeException(nameof(statusCode), "Only error status codes (400-599) can be mapped.");

            var expose = exposeMessage ?? statusCode < 500;

            _mappings[typeof(TException)] = (exception, _) => new MvcProblemDetails
            {
                Status = statusCode,
                Title = title ?? ReasonPhrases.GetReasonPhrase(statusCode),
                Detail = expose ? exception.Message : null
            };

            return this;
        }

        /// <summary>
        /// Maps <typeparamref name="TException"/> (and derived types) with a factory, for full control
        /// of the response (status, title, detail, type, extensions).
        /// </summary>
        /// <typeparam name="TException">Exception type.</typeparam>
        /// <param name="factory">Creates the problem details. <c>Status</c> defaults to 500 when not set.</param>
        /// <returns>The same options, for chaining.</returns>
        public RkdProblemDetailsOptions Map<TException>(Func<TException, MvcProblemDetails> factory)
            where TException : Exception
        {
            ArgumentNullException.ThrowIfNull(factory);

            return Map<TException>((exception, _) => factory(exception));
        }

        /// <summary>
        /// Maps <typeparamref name="TException"/> (and derived types) with a factory that also receives the
        /// <see cref="HttpContext"/> (e.g. to localize messages or read the route).
        /// </summary>
        /// <typeparam name="TException">Exception type.</typeparam>
        /// <param name="factory">Creates the problem details. <c>Status</c> defaults to 500 when not set.</param>
        /// <returns>The same options, for chaining.</returns>
        public RkdProblemDetailsOptions Map<TException>(Func<TException, HttpContext, MvcProblemDetails> factory)
            where TException : Exception
        {
            ArgumentNullException.ThrowIfNull(factory);

            _mappings[typeof(TException)] = (exception, context) => factory((TException)exception, context);

            return this;
        }

        /// <summary>
        /// Finds the mapping of the most specific type in the exception hierarchy.
        /// </summary>
        internal bool TryMap(Exception exception, HttpContext context, out MvcProblemDetails problem)
        {
            for (var type = exception.GetType(); type is not null && type != typeof(object); type = type.BaseType)
            {
                if (_mappings.TryGetValue(type, out var factory))
                {
                    problem = factory(exception, context) ?? new MvcProblemDetails();
                    problem.Status ??= StatusCodes.Status500InternalServerError;
                    return true;
                }
            }

            problem = null!;
            return false;
        }
    }
}
