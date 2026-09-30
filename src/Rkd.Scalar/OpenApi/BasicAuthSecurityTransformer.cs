using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Rkd.Scalar.Infrastructure;

namespace Rkd.Scalar.OpenApi
{
    internal sealed class BasicAuthSecurityTransformer : IOpenApiDocumentTransformer
    {
        private readonly ScalarFeatureRegistry _registry;

        public BasicAuthSecurityTransformer(ScalarFeatureRegistry registry)
        {
            _registry = registry;
        }

        public Task TransformAsync(
            OpenApiDocument document,
            OpenApiDocumentTransformerContext context,
            CancellationToken cancellationToken)
        {
            SecuritySchemeDocument.Apply(
                document,
                RkdScalarAuthenticationSchemes.Basic,
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "basic",
                    Description = "Basic Authentication."
                },
                addGlobalRequirement: !_registry.OperationLevelSecurity);

            return Task.CompletedTask;
        }
    }
}
