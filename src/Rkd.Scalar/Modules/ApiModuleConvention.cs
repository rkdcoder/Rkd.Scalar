using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Rkd.Scalar.Features;
using Rkd.Scalar.Infrastructure;

namespace Rkd.Scalar.Modules
{
    /// <summary>
    /// Applies the route template of <see cref="ApiModuleAttribute"/> controllers when the application model is built,
    /// using the global template or the versioning-aware default.
    /// </summary>
    internal sealed class ApiModuleConvention : IControllerModelConvention
    {
        private readonly ScalarFeatureRegistry _registry;

        public ApiModuleConvention(ScalarFeatureRegistry registry)
        {
            _registry = registry;
        }

        public void Apply(ControllerModel controller)
        {
            var module = controller.Attributes.OfType<ApiModuleAttribute>().FirstOrDefault();

            if (module is null)
                return;

            var template = module.RouteTemplate
                ?? _registry.ApiModules.RouteTemplate
                ?? (_registry.Features.OfType<VersioningFeature>().Any()
                    ? ApiModuleOptions.VersionedTemplate
                    : ApiModuleOptions.UnversionedTemplate);

            var resolved = ApiModuleTemplate.Resolve(template, module.Module);

            foreach (var selector in controller.Selectors)
            {
                if (selector.AttributeRouteModel?.Attribute is ApiModuleAttribute)
                    selector.AttributeRouteModel.Template = resolved;
            }
        }
    }
}
