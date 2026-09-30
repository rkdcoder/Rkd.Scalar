using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Rkd.Scalar.Infrastructure;
using Rkd.Scalar.Security;

namespace Rkd.Scalar.OpenApi
{
    /// <summary>
    /// Adds security requirements only to operations that require authorization,
    /// honoring <c>[AllowAnonymous]</c> and the schemes named in <c>[Authorize(AuthenticationSchemes = ...)]</c>.
    /// </summary>
    internal sealed class AuthorizeOperationSecurityTransformer : IOpenApiOperationTransformer
    {
        private readonly ScalarFeatureRegistry _registry;

        public AuthorizeOperationSecurityTransformer(ScalarFeatureRegistry registry)
        {
            _registry = registry;
        }

        public Task TransformAsync(
            OpenApiOperation operation,
            OpenApiOperationTransformerContext context,
            CancellationToken cancellationToken)
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;

            if (metadata.OfType<IAllowAnonymous>().Any())
                return Task.CompletedTask;

            var authorizeData = metadata.OfType<IAuthorizeData>().ToArray();
            var policies = metadata.OfType<AuthorizationPolicy>().ToArray();

            if (authorizeData.Length == 0 && policies.Length == 0)
                return Task.CompletedTask;

            var requested = authorizeData
                .SelectMany(a => (a.AuthenticationSchemes ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Concat(policies.SelectMany(p => p.AuthenticationSchemes))
                .ToHashSet(StringComparer.Ordinal);

            var schemes = requested.Count == 0 || requested.Contains(RkdScalarAuthenticationSchemes.Default)
                ? _registry.AuthenticationSchemes
                : _registry.AuthenticationSchemes.Where(requested.Contains).ToList();

            if (schemes.Count == 0)
                return Task.CompletedTask;

            operation.Security ??= new List<OpenApiSecurityRequirement>();

            foreach (var scheme in schemes)
            {
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(scheme, context.Document)] = new List<string>()
                });
            }

            return Task.CompletedTask;
        }
    }
}
