using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Rkd.Scalar.Infrastructure;
using Rkd.Scalar.Security.ApiKey;

namespace Rkd.Scalar.OpenApi
{
    internal sealed class ApiKeySecurityTransformer : IOpenApiDocumentTransformer
    {
        private const string SchemeName = RkdScalarAuthenticationSchemes.ApiKey;

        private readonly ScalarFeatureRegistry _registry;

        private readonly IOptionsMonitor<ApiKeyAuthenticationOptions> _options;

        public ApiKeySecurityTransformer(
            ScalarFeatureRegistry registry,
            IOptionsMonitor<ApiKeyAuthenticationOptions> options)
        {
            _registry = registry;
            _options = options;
        }

        public Task TransformAsync(
            OpenApiDocument document,
            OpenApiDocumentTransformerContext context,
            CancellationToken cancellationToken)
        {
            var headerName = _options.Get(SchemeName).HeaderName;

            SecuritySchemeDocument.Apply(
                document,
                SchemeName,
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    Name = headerName,
                    In = ParameterLocation.Header,
                    Description = $"API Key authentication using {headerName} header."
                },
                addGlobalRequirement: !_registry.OperationLevelSecurity);

            return Task.CompletedTask;
        }
    }
}
