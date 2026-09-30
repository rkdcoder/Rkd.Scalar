using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Rkd.Scalar.Infrastructure;
using System.Reflection;

namespace Rkd.Scalar.OpenApi.XmlComments
{
    /// <summary>
    /// Applies <c>summary</c>, <c>remarks</c>, <c>param</c>, <c>returns</c> and <c>response</c> XML comments
    /// of controller actions and minimal API handlers to the OpenAPI operations.
    /// Never overwrites descriptions that are already set (attributes, <c>WithSummary</c>, the ASP.NET source generator).
    /// </summary>
    internal sealed class XmlCommentOperationTransformer : IOpenApiOperationTransformer
    {
        private readonly ScalarFeatureRegistry _registry;

        private readonly XmlDocumentationProvider _documentation;

        public XmlCommentOperationTransformer(
            ScalarFeatureRegistry registry,
            XmlDocumentationProvider documentation)
        {
            _registry = registry;
            _documentation = documentation;
        }

        public Task TransformAsync(
            OpenApiOperation operation,
            OpenApiOperationTransformerContext context,
            CancellationToken cancellationToken)
        {
            if (!_registry.XmlComments)
                return Task.CompletedTask;

            var method = GetHandlerMethod(context);
            var docs = method is null ? null : _documentation.GetMember(method);

            if (docs is not null)
            {
                if (string.IsNullOrWhiteSpace(operation.Summary))
                    operation.Summary = docs.Summary;

                if (string.IsNullOrWhiteSpace(operation.Description))
                    operation.Description = docs.Remarks;

                ApplyResponses(operation, docs);
            }

            ApplyParameters(operation, context, docs);

            return Task.CompletedTask;
        }

        private static MethodInfo? GetHandlerMethod(OpenApiOperationTransformerContext context) =>
            context.Description.ActionDescriptor is ControllerActionDescriptor controller
                ? controller.MethodInfo
                : context.Description.ActionDescriptor.EndpointMetadata.OfType<MethodInfo>().FirstOrDefault();

        private void ApplyParameters(
            OpenApiOperation operation,
            OpenApiOperationTransformerContext context,
            XmlMemberDocumentation? docs)
        {
            foreach (var parameter in context.Description.ParameterDescriptions)
            {
                string? description;

                if (parameter.ModelMetadata is { MetadataKind: ModelMetadataKind.Property, ContainerType: { } container, PropertyName: { } propertyName } &&
                    container.GetProperty(propertyName) is { } property)
                {
                    var propertyDocs = _documentation.GetMember(property);
                    description = propertyDocs?.Summary ?? propertyDocs?.Value;
                }
                else
                {
                    description = docs?.Parameter(parameter.ParameterDescriptor?.Name ?? parameter.Name);
                }

                if (description is null)
                    continue;

                if (parameter.Source == BindingSource.Body ||
                    parameter.Source == BindingSource.Form ||
                    parameter.Source == BindingSource.FormFile)
                {
                    if (operation.RequestBody is OpenApiRequestBody body && string.IsNullOrWhiteSpace(body.Description))
                        body.Description = description;

                    continue;
                }

                var target = operation.Parameters?
                    .OfType<OpenApiParameter>()
                    .FirstOrDefault(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));

                if (target is not null && string.IsNullOrWhiteSpace(target.Description))
                    target.Description = description;
            }
        }

        private static void ApplyResponses(OpenApiOperation operation, XmlMemberDocumentation docs)
        {
            operation.Responses ??= new OpenApiResponses();

            foreach (var (code, description) in docs.Responses)
            {
                if (description is null)
                    continue;

                if (operation.Responses.TryGetValue(code, out var existing))
                {
                    if (existing is OpenApiResponse response)
                        response.Description = description;
                }
                else
                {
                    operation.Responses[code] = new OpenApiResponse { Description = description };
                }
            }

            if (docs.Returns is not { } returns)
                return;

            // <returns> documents the success response, unless it already has a meaningful description.
            var success = operation.Responses
                .Where(r => r.Key.StartsWith('2'))
                .OrderBy(r => r.Key, StringComparer.Ordinal)
                .Select(r => r.Value)
                .OfType<OpenApiResponse>()
                .FirstOrDefault();

            if (success is not null &&
                !docs.Responses.Any(r => r.Code.StartsWith('2')) &&
                IsDefaultDescription(success.Description, operation))
                success.Description = returns;
        }

        private static bool IsDefaultDescription(string? description, OpenApiOperation operation)
        {
            if (string.IsNullOrWhiteSpace(description))
                return true;

            return operation.Responses!.Any(r =>
                int.TryParse(r.Key, out var status) &&
                string.Equals(ReasonPhrases.GetReasonPhrase(status), description, StringComparison.Ordinal));
        }
    }
}
