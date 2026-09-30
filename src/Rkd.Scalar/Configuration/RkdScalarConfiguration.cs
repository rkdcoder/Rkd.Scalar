using Scalar.AspNetCore;

namespace Rkd.Scalar.Configuration
{
    /// <summary>
    /// Options applied by <c>UseRkdScalar</c>. Can be bound from the <c>RkdScalar</c> configuration section.
    /// </summary>
    public sealed class RkdScalarConfiguration
    {
        /// <summary>
        /// Route pattern of the OpenAPI documents. Must contain <c>{documentName}</c>.
        /// </summary>
        public string OpenApiRoutePattern { get; set; } = "/openapi/{documentName}.json";

        /// <summary>
        /// Route prefix of the Scalar UI. Defaults to <c>/scalar</c>.
        /// </summary>
        public string ScalarRoutePrefix { get; set; } = "/scalar";

        /// <summary>
        /// Title shown by the Scalar UI.
        /// </summary>
        public string Title { get; set; } = "API Documentation.";

        /// <summary>
        /// Scalar UI theme.
        /// </summary>
        public ScalarTheme Theme { get; set; } = ScalarTheme.Default;

        /// <summary>
        /// When <see langword="false"/>, the OpenAPI documents and the Scalar UI are not mapped
        /// (authentication and the login endpoint keep working). Handy to turn documentation off per
        /// environment, e.g. <c>"RkdScalar": { "Enabled": false }</c> in appsettings.Production.json.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Direct access to the Scalar options, applied after the settings above.
        /// </summary>
        public Action<ScalarOptions>? ConfigureScalar { get; set; }
    }
}
