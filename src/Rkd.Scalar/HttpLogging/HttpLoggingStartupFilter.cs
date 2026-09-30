using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Rkd.Scalar.HttpLogging
{
    /// <summary>
    /// Places the HTTP logging middleware first in the pipeline (outside the problem details middleware), so the
    /// entry has the final status code and body the client received, wherever <c>UseRkdScalar</c> is called.
    /// </summary>
    internal sealed class HttpLoggingStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.UseMiddleware<HttpLoggingMiddleware>();
                next(app);
            };
    }
}
