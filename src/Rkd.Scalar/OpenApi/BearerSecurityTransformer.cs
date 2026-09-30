using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Rkd.Scalar.Infrastructure;
using Rkd.Scalar.Security;

namespace Rkd.Scalar.OpenApi
{
    internal sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer
    {
        private readonly ScalarFeatureRegistry _registry;

        public BearerSecurityTransformer(ScalarFeatureRegistry registry)
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
                RkdScalarAuthenticationSchemes.Bearer,
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Enter the JWT token."
                },
                addGlobalRequirement: !_registry.OperationLevelSecurity);

            return Task.CompletedTask;
        }
    }
}
