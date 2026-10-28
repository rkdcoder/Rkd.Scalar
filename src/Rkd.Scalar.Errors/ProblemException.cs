using Microsoft.AspNetCore.Http;

namespace Rkd.Scalar
{
    /// <summary>
    /// Exception converted into an RFC 9457 problem details response by <c>WithProblemDetails()</c>,
    /// with the given status, title, detail, type and extensions.
    /// </summary>
    /// <remarks>
    /// Everything set here is sent to the client, so only use it for information meant to be public.
    /// </remarks>
    /// <example>
    /// <code>
    /// throw new ProblemException(StatusCodes.Status404NotFound, "Order not found", $"Order {id} does not exist.")
    /// {
    ///     Extensions = { ["orderId"] = id }
    /// };
    /// </code>
    /// </example>
    public class ProblemException : Exception, IProblemLogDetails
    {
        /// <summary>
        /// Creates a problem with the given status code.
        /// </summary>
        /// <param name="statusCode">HTTP status code (400–599).</param>
        /// <param name="title">Short, human-readable summary. Defaults to the status code reason phrase.</param>
        /// <param name="detail">Explanation specific to this occurrence.</param>
        /// <param name="innerException">Optional cause, logged but never sent to the client.</param>
        public ProblemException(int statusCode, string? title = null, string? detail = null, Exception? innerException = null)
            : base(detail ?? title ?? $"HTTP {statusCode}", innerException)
        {
            if (statusCode is < 400 or > 599)
                throw new ArgumentOutOfRangeException(nameof(statusCode), "Problem status codes must be between 400 and 599.");

            StatusCode = statusCode;
            Title = title;
            Detail = detail;
        }

        /// <summary>HTTP status code of the response.</summary>
        public int StatusCode { get; }

        /// <summary>Short, human-readable summary of the problem type.</summary>
        public string? Title { get; }

        /// <summary>Explanation specific to this occurrence of the problem.</summary>
        public string? Detail { get; }

        /// <summary>URI reference that identifies the problem type (e.g. a page of your error catalog).</summary>
        public string? Type { get; init; }

        /// <summary>Additional members of the problem details object (e.g. <c>orderId</c>, <c>errors</c>).</summary>
        public IDictionary<string, object?> Extensions { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

        /// <summary>
        /// Stable, machine-readable error code written as the <c>code</c> member (e.g. <c>CUSTOMER_ALREADY_EXISTS</c>).
        /// Clients should switch on it instead of on the message. See <see cref="RkdError"/>.
        /// </summary>
        public string? Code
        {
            get => _code;
            init => _code = value is null ? null : ValidateCode(value);
        }

        private readonly string? _code;

        // Letters, digits, '_', '-' and '.' only, so the code is safe to switch on. Same rule as the codes
        // configured in Rkd.Scalar (ProblemCodes.Validate); a test keeps both in sync.
        private static string ValidateCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("The error code cannot be empty.", nameof(code));

            foreach (var character in code)
            {
                if (!char.IsLetterOrDigit(character) && character is not ('_' or '-' or '.'))
                    throw new ArgumentException(
                        $"'{code}' is not a valid error code. Use letters, digits, '_', '-' or '.', e.g. \"CUSTOMER_NOT_FOUND\".",
                        nameof(code));
            }

            return code;
        }

        /// <summary>
        /// Technical details written to the log and <b>never</b> sent to the client (ids, upstream errors…).
        /// The <see cref="Exception.InnerException"/>, when present, is logged as well.
        /// </summary>
        public string? LogDetails { get; set; }

        /// <summary>
        /// Sets <see cref="LogDetails"/> and returns the same exception, for use with <see cref="RkdError"/>:
        /// <c>throw RkdError.Conflict("CODE", "…").WithLogDetails($"tenant={tenant}");</c>.
        /// </summary>
        /// <param name="logDetails">Details written only to the log.</param>
        /// <returns>The same exception.</returns>
        public ProblemException WithLogDetails(string? logDetails)
        {
            LogDetails = logDetails;
            return this;
        }

        /// <summary>
        /// Converts the exception into the problem details that will be sent to the client.
        /// </summary>
        public Microsoft.AspNetCore.Mvc.ProblemDetails ToProblemDetails()
        {
            var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = StatusCode,
                Title = Title,
                Detail = Detail,
                Type = Type
            };

            foreach (var (key, value) in Extensions)
                problem.Extensions[key] = value;

            if (Code is not null)
                problem.Extensions[Rkd.Problems.ProblemMembers.Code] = Code;

            return problem;
        }

        /// <summary>Creates a 400 Bad Request problem.</summary>
        public static ProblemException BadRequest(string? detail = null) => new(StatusCodes.Status400BadRequest, detail: detail);

        /// <summary>Creates a 403 Forbidden problem.</summary>
        public static ProblemException Forbidden(string? detail = null) => new(StatusCodes.Status403Forbidden, detail: detail);

        /// <summary>Creates a 404 Not Found problem.</summary>
        public static ProblemException NotFound(string? detail = null) => new(StatusCodes.Status404NotFound, detail: detail);

        /// <summary>Creates a 409 Conflict problem.</summary>
        public static ProblemException Conflict(string? detail = null) => new(StatusCodes.Status409Conflict, detail: detail);

        /// <summary>Creates a 422 Unprocessable Entity problem.</summary>
        public static ProblemException UnprocessableEntity(string? detail = null) => new(StatusCodes.Status422UnprocessableEntity, detail: detail);
    }
}
