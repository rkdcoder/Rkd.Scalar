using Microsoft.AspNetCore.Diagnostics;

namespace Rkd.Scalar.Errors
{
    /// <summary>
    /// In Development, ASP.NET Core adds the developer exception page inside the pipeline, where it catches
    /// exceptions before <see cref="ProblemDetailsMiddleware"/>. This filter makes it produce the same
    /// problem details (mappings included, plus exception details), so Development behaves like production.
    /// </summary>
    internal sealed class ProblemDetailsDeveloperPageFilter : IDeveloperPageExceptionFilter
    {
        private readonly ProblemDetailsExceptionHandler _handler;

        public ProblemDetailsDeveloperPageFilter(ProblemDetailsExceptionHandler handler)
        {
            _handler = handler;
        }

        public Task HandleExceptionAsync(ErrorContext errorContext, Func<ErrorContext, Task> next) =>
            _handler.HandleAsync(errorContext.HttpContext, errorContext.Exception);
    }
}
