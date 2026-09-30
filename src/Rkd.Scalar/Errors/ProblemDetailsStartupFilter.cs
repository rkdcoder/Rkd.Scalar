using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Rkd.Scalar.Errors
{
    /// <summary>
    /// Places <see cref="ProblemDetailsMiddleware"/> at the very beginning of the pipeline, so it catches
    /// exceptions and error responses from every middleware and endpoint, wherever <c>UseRkdScalar</c> is called.
    /// </summary>
    internal sealed class ProblemDetailsStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.UseMiddleware<ProblemDetailsMiddleware>();
                next(app);
            };
    }
}
