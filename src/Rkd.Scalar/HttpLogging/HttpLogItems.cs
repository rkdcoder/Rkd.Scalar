using Microsoft.AspNetCore.Http;
using MvcProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Rkd.Scalar.HttpLogging
{
    /// <summary>
    /// Data the problem details feature leaves for the HTTP log: the problem written (its <c>code</c>) and the
    /// exception it handled (the exception never reaches the logging middleware, which is outside it).
    /// </summary>
    internal static class HttpLogItems
    {
        private static readonly object ProblemKey = new();

        private static readonly object ExceptionKey = new();

        public static void SetProblem(HttpContext context, MvcProblemDetails problem) => context.Items[ProblemKey] = problem;

        public static void SetException(HttpContext context, Exception exception) => context.Items[ExceptionKey] = exception;

        public static string? GetErrorCode(HttpContext context) =>
            context.Items.TryGetValue(ProblemKey, out var value) &&
            value is MvcProblemDetails problem &&
            problem.Extensions.TryGetValue(Errors.ProblemCodes.ExtensionName, out var code)
                ? code?.ToString()
                : null;

        public static Exception? GetException(HttpContext context) =>
            context.Items.TryGetValue(ExceptionKey, out var value) ? value as Exception : null;
    }
}
