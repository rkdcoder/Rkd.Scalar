using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using MvcProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Rkd.Scalar.Errors
{
    /// <summary>
    /// MVC reports an exceeded form limit (<c>MultipartBodyLengthLimit</c>, <c>ValueCountLimit</c>…) as a model
    /// validation error ("Failed to read the request form. … limit … exceeded."); this filter answers 413 instead,
    /// like minimal APIs and code that reads the form directly. Runs before <c>[ApiController]</c>'s validation filter.
    /// </summary>
    internal sealed class FormLimitActionFilter : IActionFilter, IOrderedFilter
    {
        private const string FormReadFailure = "Failed to read the request form.";

        public int Order => -2100;

        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (!context.ModelState.TryGetValue(string.Empty, out var entry))
                return;

            foreach (var error in entry.Errors)
            {
                var message = error.ErrorMessage;

                if (message.StartsWith(FormReadFailure, StringComparison.Ordinal) &&
                    message.Contains("limit", StringComparison.OrdinalIgnoreCase))
                {
                    context.Result = new RkdProblemResult(new MvcProblemDetails
                    {
                        Status = StatusCodes.Status413PayloadTooLarge,
                        Detail = "The request body is larger than the maximum allowed size."
                    });
                    return;
                }
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}
