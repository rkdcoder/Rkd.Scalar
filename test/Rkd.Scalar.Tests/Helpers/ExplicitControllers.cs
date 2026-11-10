using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace Rkd.Scalar.Tests.Helpers
{
    /// <summary>
    /// Adds <c>[NonController]</c> test controllers by hand. They stay out of discovery, so a controller with a route that
    /// breaks the startup (or changes the binding) on purpose only exists in the tests that ask for it.
    /// </summary>
    public sealed class ExplicitControllers(params Type[] controllers) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in controllers)
                feature.Controllers.Add(controller.GetTypeInfo());
        }
    }

    public static class ExplicitControllersExtensions
    {
        /// <summary><c>AddControllers()</c> plus <paramref name="controllers"/>, which are <c>[NonController]</c>.</summary>
        public static IMvcBuilder AddExplicitControllers(this IServiceCollection services, params Type[] controllers) =>
            services.AddControllers()
                .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new ExplicitControllers(controllers)));
    }
}
