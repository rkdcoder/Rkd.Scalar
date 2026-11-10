using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Rkd.Scalar.Infrastructure;

namespace Rkd.Scalar.Modules
{
    /// <summary>
    /// Applies the effective route of <see cref="ApiModuleAttribute"/> controllers (<see cref="ApiModuleConvention"/>) right
    /// after ASP.NET Core creates the controller models, instead of only at the end, when the conventions run.
    /// </summary>
    /// <remarks>
    /// In between, ASP.NET Core infers where each simple parameter of an API controller comes from by reading its routes,
    /// and the attribute alone only knows the default versioned template. Without this, in an application without
    /// <c>WithVersioning</c> an action route <c>{version}</c> stopped the startup (the parameter seemed repeated) and a
    /// <c>version</c> parameter was bound from the route instead of the query string; a parameter of the global template
    /// (<c>WithApiModules</c>, e.g. <c>{tenant}</c>) was bound from the query string instead of the route. The convention
    /// still runs at the end, with the same result.
    /// </remarks>
    internal sealed class ApiModuleApplicationModelProvider(ScalarFeatureRegistry registry) : IApplicationModelProvider
    {
        private readonly ApiModuleConvention _convention = new(registry);

        /// <summary>Right after DefaultApplicationModelProvider (-1000), before everything that reads the routes.</summary>
        public int Order => -1000 + 1;

        public void OnProvidersExecuting(ApplicationModelProviderContext context)
        {
            foreach (var controller in context.Result.Controllers)
                _convention.Apply(controller);
        }

        public void OnProvidersExecuted(ApplicationModelProviderContext context)
        {
        }
    }
}
