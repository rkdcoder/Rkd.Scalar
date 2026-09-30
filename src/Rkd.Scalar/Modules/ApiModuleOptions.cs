namespace Rkd.Scalar
{
    /// <summary>
    /// Global options of <see cref="ApiModuleAttribute"/>, set with <c>WithApiModules</c>.
    /// </summary>
    public sealed class ApiModuleOptions
    {
        /// <summary>Default template when <c>WithVersioning</c> is enabled.</summary>
        public const string VersionedTemplate = "api/v{version:apiVersion}/[module]/[controller]";

        /// <summary>Default template when versioning is not enabled.</summary>
        public const string UnversionedTemplate = "api/[module]/[controller]";

        private string? _routeTemplate;

        /// <summary>
        /// Route template of every <c>[ApiModule]</c> controller, with the <c>[module]</c> token
        /// (for example <c>"v{version:apiVersion}/[module]/[controller]"</c> or <c>"[module]/[controller]"</c>).
        /// When <see langword="null"/> (default), <see cref="VersionedTemplate"/> or <see cref="UnversionedTemplate"/>
        /// is used depending on whether <c>WithVersioning</c> is enabled.
        /// </summary>
        public string? RouteTemplate
        {
            get => _routeTemplate;
            set
            {
                if (value is not null)
                    ApiModuleTemplate.Validate(value);

                _routeTemplate = value;
            }
        }
    }
}
