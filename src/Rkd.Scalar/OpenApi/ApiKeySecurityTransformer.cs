using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Rkd.Scalar.OpenApi
{
    internal sealed class ApiKeySecurityTransformer : IOpenApiDocumentTransformer
    {
        private const string SchemeName = "ApiKey";

        public Task TransformAsync(
            OpenApiDocument document,
            OpenApiDocumentTransformerContext context,
            CancellationToken cancellationToken)
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

            if (!document.Components.SecuritySchemes.ContainsKey(SchemeName))
            {
                document.Components.SecuritySchemes[SchemeName] =
                    new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.ApiKey,
                        Name = "X-API-Key",
                        In = ParameterLocation.Header,
                        Description = "API Key authentication using X-API-Key header."
                    };
            }

            document.Security ??= new List<OpenApiSecurityRequirement>();

            var schemeReference = new OpenApiSecuritySchemeReference(SchemeName);

            if (!document.Security.Any(r => r.ContainsKey(schemeReference)))
            {
                document.Security.Add(new OpenApiSecurityRequirement
                {
                    [schemeReference] = new List<string>()
                });
            }

            return Task.CompletedTask;
        }
    }
}
