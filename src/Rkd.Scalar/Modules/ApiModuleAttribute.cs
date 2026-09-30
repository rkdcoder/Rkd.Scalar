using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Rkd.Scalar
{
    /// <summary>
    /// Groups a controller into an API module: applies <c>[ApiController]</c>, a standardized route
    /// (<c>api/v{version:apiVersion}/[module]/[controller]</c> by default) and the module as its OpenAPI / Scalar tag.
    /// </summary>
    /// <remarks>
    /// The route template comes, in order, from <see cref="RouteTemplate"/>, from
    /// <c>WithApiModules(o => o.RouteTemplate = ...)</c>, or from the default, which includes
    /// <c>v{version:apiVersion}</c> only when <c>WithVersioning</c> is enabled.
    /// Do not combine with a class-level <c>[Route]</c>.
    /// </remarks>
    /// <example>
    /// <code>
    /// [ApiModule("billing")]              // api/v1/billing/invoices, tag "billing"
    /// [ApiVersion("1.0")]
    /// public class InvoicesController : ControllerBase { ... }
    ///
    /// [ApiModule("billing", Tag = "Billing", RouteTemplate = "internal/[module]/[controller]")]
    /// public class ReportsController : ControllerBase { ... }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class ApiModuleAttribute : ApiControllerAttribute, IRouteTemplateProvider, ITagsMetadata
    {
        /// <summary>Token replaced by the module name in route templates.</summary>
        public const string ModuleToken = "[module]";

        private int? _order;

        /// <summary>
        /// Creates the attribute for <paramref name="module"/>.
        /// </summary>
        /// <param name="module">
        /// Module name used in the route and as tag, e.g. <c>billing</c>, <c>finance/reports</c> or
        /// <c>tools/{toolId}</c> (route parameters are allowed; they are left out of the default tag).
        /// </param>
        public ApiModuleAttribute(string module)
        {
            Module = ApiModuleTemplate.NormalizeModule(module);
        }

        /// <summary>The module name.</summary>
        public string Module { get; }

        /// <summary>
        /// Route template of this controller, with the <c>[module]</c> token (for example
        /// <c>"internal/[module]/[controller]"</c>). When <see langword="null"/>, the global template is used.
        /// </summary>
        public string? RouteTemplate { get; set; }

        /// <summary>OpenAPI / Scalar tag. Defaults to <see cref="Module"/> without its route parameters.</summary>
        public string? Tag { get; set; }

        /// <summary>Route order, as in <c>[Route(Order = ...)]</c>.</summary>
        public int Order
        {
            get => _order ?? 0;
            set => _order = value;
        }

        /// <summary>Route name, as in <c>[Route(Name = ...)]</c>.</summary>
        public string? Name { get; set; }

        /// <summary>
        /// The resolved template. Rkd.Scalar replaces it at startup when a global template or versioning
        /// applies; this value is the fallback when the application model convention is not registered.
        /// </summary>
        public string Template => ApiModuleTemplate.Resolve(RouteTemplate ?? ApiModuleOptions.VersionedTemplate, Module);

        /// <summary>The OpenAPI / Scalar tags of the controller.</summary>
        public IReadOnlyList<string> Tags => [Tag ?? ApiModuleTemplate.DefaultTag(Module)];

        int? IRouteTemplateProvider.Order => _order;
    }
}
