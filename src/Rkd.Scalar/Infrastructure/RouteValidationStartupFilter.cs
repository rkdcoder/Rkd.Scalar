using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Rkd.Scalar.Infrastructure
{
    /// <summary>
    /// Route checks that need every endpoint, run when the request pipeline is built, before the server starts listening:
    /// by then every <c>Map*</c> call of the application has registered its endpoints, wherever (and whether)
    /// <c>UseRkdScalar</c> is called and whatever <c>RkdScalar:Enabled</c> says.
    /// <list type="bullet">
    /// <item>the late half of <see cref="RouteTemplateValidation"/> (invalid attribute routes);</item>
    /// <item>the endpoints of the application under the paths of the documentation (<see cref="ReservedRouteGuard"/>), as warnings.</item>
    /// </list>
    /// </summary>
    internal sealed class RouteValidationStartupFilter(ScalarFeatureRegistry registry) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                if (registry.RouteValidation)
                {
                    try
                    {
                        next(app);
                    }
                    catch (Exception exception)
                    {
                        // The route table may already be built while the pipeline is configured (UseEndpoints rebuilds it
                        // when something read it before): the same failure, explained.
                        RouteTemplateValidation.ThrowIfTemplatesAreInvalid(app.ApplicationServices, exception);
                        throw;
                    }

                    RouteTemplateValidation.EnsureEndpointsCanBeBuilt(app.ApplicationServices);
                }
                else
                {
                    next(app);
                }

                ReservedRouteGuard.WarnAboutEndpointsUnderTheDocumentation(app.ApplicationServices, registry.DocumentationPaths);
            };
    }
}
