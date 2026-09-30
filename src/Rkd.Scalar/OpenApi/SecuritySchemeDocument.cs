using Microsoft.OpenApi;

namespace Rkd.Scalar.OpenApi
{
    internal static class SecuritySchemeDocument
    {
        /// <summary>
        /// Adds <paramref name="scheme"/> to the document components and, unless security is applied
        /// per operation, adds a global security requirement for it.
        /// </summary>
        public static void Apply(
            OpenApiDocument document,
            string schemeName,
            OpenApiSecurityScheme scheme,
            bool addGlobalRequirement)
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

            if (!document.Components.SecuritySchemes.ContainsKey(schemeName))
                document.Components.SecuritySchemes[schemeName] = scheme;

            if (!addGlobalRequirement)
                return;

            document.Security ??= new List<OpenApiSecurityRequirement>();

            var schemeReference = new OpenApiSecuritySchemeReference(schemeName);

            if (!document.Security.Any(r => r.ContainsKey(schemeReference)))
            {
                document.Security.Add(new OpenApiSecurityRequirement
                {
                    [schemeReference] = new List<string>()
                });
            }
        }
    }
}
