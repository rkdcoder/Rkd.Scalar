using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Rkd.Scalar.Infrastructure
{
    /// <summary>
    /// The early half of <see cref="RouteTemplateValidation"/>. On API controllers (<c>[ApiController]</c>,
    /// <see cref="ApiModuleAttribute"/>), ASP.NET Core parses the attribute routes while it builds the application model
    /// (inside <c>MapControllers</c>) to infer where each simple parameter comes from, and an invalid template stops the
    /// application with "The route parameter name 'x' appears more than one time in the route template", without the
    /// action or the template. This provider runs just before that step, parses the same templates of the same actions and
    /// throws the explanation instead. Actions that ASP.NET Core would not parse there are left to the startup check.
    /// </summary>
    /// <remarks>
    /// Only <see cref="IServiceProvider"/> in the constructor: AddRkdScalar registers this provider in applications without
    /// MVC too (minimal APIs), where the MVC services do not exist and ValidateOnBuild (Development) checks every registration.
    /// </remarks>
    internal sealed class RouteValidationApplicationModelProvider(ScalarFeatureRegistry registry, IServiceProvider services)
        : IApplicationModelProvider
    {
        /// <summary>After DefaultApplicationModelProvider (-1000) builds the routes, before ApiBehaviorApplicationModelProvider (-900) parses them.</summary>
        public int Order => -1000 + 99;

        public void OnProvidersExecuting(ApplicationModelProviderContext context)
        {
            if (!registry.RouteValidation
                || services.GetService<IModelMetadataProvider>() is not { } metadata
                || services.GetService<IOptions<ApiBehaviorOptions>>()?.Value.SuppressInferBindingSourcesForParameters != false)
                return;

            var isService = services.GetService<IServiceProviderIsService>();
            var problems = new List<RouteTemplateValidation.InvalidTemplate>();

            foreach (var controller in context.Result.Controllers)
            {
                if (!IsApiController(controller))
                    continue;

                foreach (var action in controller.Actions)
                {
                    // ASP.NET Core only parses the routes of an action to infer a simple parameter without a binding source.
                    if (!action.Parameters.Any(parameter => IsInferredFromTheRoute(parameter, metadata, isService)))
                        continue;

                    foreach (var template in CombinedTemplates(action).Distinct(StringComparer.Ordinal))
                    {
                        if (RouteTemplateValidation.ParseError(template) is { } error)
                            problems.Add(new RouteTemplateValidation.InvalidTemplate(action.DisplayName, template, error, RouteTemplateValidation.Hint(template)));
                    }
                }
            }

            if (problems.Count > 0)
                throw new InvalidOperationException(RouteTemplateValidation.Describe(problems, whileBuildingControllers: true));
        }

        public void OnProvidersExecuted(ApplicationModelProviderContext context)
        {
        }

        private static bool IsInferredFromTheRoute(ParameterModel parameter, IModelMetadataProvider metadata, IServiceProviderIsService? isService) =>
            parameter.BindingInfo?.BindingSource is null
            && !metadata.GetMetadataForType(parameter.ParameterType).IsComplexType
            && isService?.IsService(parameter.ParameterType) != true;

        private static bool IsApiController(ControllerModel controller) =>
            controller.Attributes.OfType<IApiBehaviorMetadata>().Any()
            || controller.ControllerType.Assembly.GetCustomAttributes(inherit: false).OfType<IApiBehaviorMetadata>().Any();

        /// <summary>The templates ASP.NET Core combines for the action: controller route + action route, or the action's own when absolute.</summary>
        private static IEnumerable<string> CombinedTemplates(ActionModel action)
        {
            var controllerHasRoutes = action.Controller.Selectors.Any(selector => selector.AttributeRouteModel is not null);

            foreach (var selector in action.Selectors)
            {
                var route = selector.AttributeRouteModel;

                if (route is { IsAbsoluteTemplate: true } || !controllerHasRoutes)
                {
                    if (route?.Template is { } own)
                        yield return own;

                    continue;
                }

                foreach (var controllerSelector in action.Controller.Selectors)
                {
                    if (AttributeRouteModel.CombineAttributeRouteModel(controllerSelector.AttributeRouteModel, route)?.Template is { } combined)
                        yield return combined;
                }
            }
        }
    }
}
