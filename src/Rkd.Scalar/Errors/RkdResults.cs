using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Rkd.Scalar.Errors;

namespace Rkd.Scalar
{
    /// <summary>
    /// Returns RFC 9457 problem details with a stable error <c>code</c>, from controllers and minimal APIs alike.
    /// Success responses keep the standard helpers (<c>Ok</c>, <c>Created</c>, <c>NoContent</c>, <c>TypedResults</c>).
    /// </summary>
    /// <example>
    /// <code>
    /// // controller: IActionResult or ActionResult&lt;Customer&gt;
    /// if (customer is null) return RkdResults.NotFound("CUSTOMER_NOT_FOUND", $"Customer {id} was not found.");
    ///
    /// // minimal API: IResult or Results&lt;Ok&lt;Customer&gt;, RkdProblemResult&gt;
    /// app.MapGet("/customers/{id}", (int id) => customer is null
    ///     ? RkdResults.NotFound("CUSTOMER_NOT_FOUND")
    ///     : Results.Ok(customer));
    /// </code>
    /// </example>
    /// <remarks>To throw instead of returning, use <see cref="RkdError"/>.</remarks>
    public static class RkdResults
    {
        /// <summary>Problem with any error status code (400–599).</summary>
        public static RkdProblemResult Problem(int statusCode, string code, string? detail = null, string? title = null) =>
            From(RkdError.Create(statusCode, code, detail, title));

        /// <summary>400 Bad Request.</summary>
        public static RkdProblemResult BadRequest(string code, string? detail = null, string? title = null) =>
            From(RkdError.BadRequest(code, detail, title));

        /// <summary>401 Unauthorized.</summary>
        public static RkdProblemResult Unauthorized(string code, string? detail = null, string? title = null) =>
            From(RkdError.Unauthorized(code, detail, title));

        /// <summary>403 Forbidden.</summary>
        public static RkdProblemResult Forbidden(string code, string? detail = null, string? title = null) =>
            From(RkdError.Forbidden(code, detail, title));

        /// <summary>404 Not Found.</summary>
        public static RkdProblemResult NotFound(string code, string? detail = null, string? title = null) =>
            From(RkdError.NotFound(code, detail, title));

        /// <summary>409 Conflict.</summary>
        public static RkdProblemResult Conflict(string code, string? detail = null, string? title = null) =>
            From(RkdError.Conflict(code, detail, title));

        /// <summary>422 Unprocessable Entity.</summary>
        public static RkdProblemResult UnprocessableEntity(string code, string? detail = null, string? title = null) =>
            From(RkdError.UnprocessableEntity(code, detail, title));

        /// <summary>429 Too Many Requests.</summary>
        public static RkdProblemResult TooManyRequests(string code, string? detail = null, string? title = null) =>
            From(RkdError.TooManyRequests(code, detail, title));

        /// <summary>Validation problem with <c>errors</c> by field (see <see cref="RkdError.Validation"/>).</summary>
        public static RkdProblemResult Validation(
            IDictionary<string, string[]> errors,
            string? detail = null,
            string code = ProblemCodes.Validation,
            int statusCode = StatusCodes.Status400BadRequest) =>
            From(RkdError.Validation(errors, detail, code, statusCode));

        /// <summary>Returns a <see cref="ProblemException"/> instead of throwing it.</summary>
        public static RkdProblemResult From(ProblemException problem)
        {
            ArgumentNullException.ThrowIfNull(problem);

            return new RkdProblemResult(problem.ToProblemDetails());
        }
    }

    /// <summary>
    /// Problem details result usable as the return value of controllers (<c>IActionResult</c>, <c>ActionResult&lt;T&gt;</c>)
    /// and minimal APIs (<c>IResult</c>, <c>Results&lt;…&gt;</c>). Written as <c>application/problem+json</c>, enriched by
    /// <c>WithProblemDetails()</c> (<c>instance</c>, <c>traceId</c>, customizations) when it is enabled.
    /// </summary>
    public sealed class RkdProblemResult : ActionResult, IResult, IStatusCodeHttpResult, IValueHttpResult, IValueHttpResult<ProblemDetails>
    {
        private static readonly ProblemDetailsWriter Writer = new();

        internal RkdProblemResult(ProblemDetails problemDetails)
        {
            ProblemDetails = problemDetails;
        }

        /// <summary>The problem that will be written.</summary>
        public ProblemDetails ProblemDetails { get; }

        /// <summary>HTTP status code of the response.</summary>
        public int StatusCode => ProblemDetails.Status ?? StatusCodes.Status500InternalServerError;

        int? IStatusCodeHttpResult.StatusCode => StatusCode;

        object? IValueHttpResult.Value => ProblemDetails;

        ProblemDetails? IValueHttpResult<ProblemDetails>.Value => ProblemDetails;

        /// <inheritdoc />
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            return Writer.WriteAsync(httpContext, ProblemDetails);
        }

        /// <inheritdoc />
        public override Task ExecuteResultAsync(ActionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return ExecuteAsync(context.HttpContext);
        }
    }
}
