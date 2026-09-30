using Microsoft.AspNetCore.Http;
using Rkd.Scalar.Errors;

namespace Rkd.Scalar
{
    /// <summary>
    /// Creates <see cref="ProblemException"/>s with a stable error <c>code</c>, to be thrown from any layer and
    /// converted into RFC 9457 problem details by <c>WithProblemDetails()</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// public static class CustomerErrors
    /// {
    ///     public const string AlreadyExists = "CUSTOMER_ALREADY_EXISTS";
    /// }
    ///
    /// throw RkdError.Conflict(CustomerErrors.AlreadyExists, "A customer with this document already exists.");
    /// // 409 { "code": "CUSTOMER_ALREADY_EXISTS", "title": "Conflict", "detail": "...", "traceId": "..." }
    /// </code>
    /// </example>
    /// <remarks>To return the problem instead of throwing, use <see cref="RkdResults"/>.</remarks>
    public static class RkdError
    {
        /// <summary>Title of validation problems, the same used by ASP.NET Core.</summary>
        public const string ValidationTitle = "One or more validation errors occurred.";

        /// <summary>Creates a problem with any error status code (400–599).</summary>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="code">Stable error code, e.g. <c>CUSTOMER_NOT_FOUND</c>.</param>
        /// <param name="detail">Explanation for this occurrence, safe to show to the client.</param>
        /// <param name="title">Short summary. Defaults to the status code reason phrase.</param>
        public static ProblemException Create(int statusCode, string code, string? detail = null, string? title = null) =>
            new(statusCode, title, detail) { Code = code };

        /// <summary>400 Bad Request.</summary>
        public static ProblemException BadRequest(string code, string? detail = null, string? title = null) =>
            Create(StatusCodes.Status400BadRequest, code, detail, title);

        /// <summary>401 Unauthorized.</summary>
        public static ProblemException Unauthorized(string code, string? detail = null, string? title = null) =>
            Create(StatusCodes.Status401Unauthorized, code, detail, title);

        /// <summary>403 Forbidden.</summary>
        public static ProblemException Forbidden(string code, string? detail = null, string? title = null) =>
            Create(StatusCodes.Status403Forbidden, code, detail, title);

        /// <summary>404 Not Found.</summary>
        public static ProblemException NotFound(string code, string? detail = null, string? title = null) =>
            Create(StatusCodes.Status404NotFound, code, detail, title);

        /// <summary>409 Conflict.</summary>
        public static ProblemException Conflict(string code, string? detail = null, string? title = null) =>
            Create(StatusCodes.Status409Conflict, code, detail, title);

        /// <summary>422 Unprocessable Entity — the request is well formed but violates a business rule.</summary>
        public static ProblemException UnprocessableEntity(string code, string? detail = null, string? title = null) =>
            Create(StatusCodes.Status422UnprocessableEntity, code, detail, title);

        /// <summary>429 Too Many Requests.</summary>
        public static ProblemException TooManyRequests(string code, string? detail = null, string? title = null) =>
            Create(StatusCodes.Status429TooManyRequests, code, detail, title);

        /// <summary>
        /// Validation problem with the same shape as ASP.NET Core's <c>ValidationProblemDetails</c>
        /// (<c>errors</c> by field). Use it to plug any validation library (FluentValidation, RuleWeaver…)
        /// into the standard error contract.
        /// </summary>
        /// <param name="errors">Messages by field name, e.g. <c>{ ["email"] = ["Invalid e-mail."] }</c>.</param>
        /// <param name="detail">Optional explanation.</param>
        /// <param name="code">Error code. Defaults to <c>VALIDATION_ERROR</c>.</param>
        /// <param name="statusCode">400 (default) or 422.</param>
        public static ProblemException Validation(
            IDictionary<string, string[]> errors,
            string? detail = null,
            string code = ProblemCodes.Validation,
            int statusCode = StatusCodes.Status400BadRequest)
        {
            ArgumentNullException.ThrowIfNull(errors);

            var problem = Create(statusCode, code, detail, ValidationTitle);
            problem.Extensions["errors"] = new Dictionary<string, string[]>(errors, StringComparer.Ordinal);

            return problem;
        }
    }
}
