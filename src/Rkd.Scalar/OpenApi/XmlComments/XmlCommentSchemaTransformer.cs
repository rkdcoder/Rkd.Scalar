using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Rkd.Scalar.Infrastructure;
using System.Reflection;

namespace Rkd.Scalar.OpenApi.XmlComments
{
    /// <summary>
    /// Applies the XML comments of types and properties to the OpenAPI schemas.
    /// Never overwrites descriptions that are already set.
    /// </summary>
    internal sealed class XmlCommentSchemaTransformer : IOpenApiSchemaTransformer
    {
        private const string SchemaIdKey = "x-schema-id";

        private const string RefDescriptionKey = "x-ref-description";

        private readonly ScalarFeatureRegistry _registry;

        private readonly XmlDocumentationProvider _documentation;

        public XmlCommentSchemaTransformer(
            ScalarFeatureRegistry registry,
            XmlDocumentationProvider documentation)
        {
            _registry = registry;
            _documentation = documentation;
        }

        public Task TransformAsync(
            OpenApiSchema schema,
            OpenApiSchemaTransformerContext context,
            CancellationToken cancellationToken)
        {
            if (!_registry.XmlComments)
                return Task.CompletedTask;

            var typeDescription = context.ParameterDescription is null
                ? _documentation.GetMember(context.JsonTypeInfo.Type)?.Summary
                : null;

            if (context.JsonPropertyInfo is { AttributeProvider: MemberInfo member and (PropertyInfo or FieldInfo) })
            {
                var docs = _documentation.GetMember(member);
                var propertyDescription = docs?.Summary ?? docs?.Value;

                if (IsReferencedSchema(schema))
                {
                    // Complex types become shared components: the property description goes next to the
                    // $ref (x-ref-description) and the component keeps the description of its type.
                    if (propertyDescription is not null)
                    {
                        schema.Metadata ??= new Dictionary<string, object>();
                        schema.Metadata.TryAdd(RefDescriptionKey, propertyDescription);
                    }

                    if (string.IsNullOrWhiteSpace(schema.Description))
                        schema.Description = typeDescription;
                }
                else if (string.IsNullOrWhiteSpace(schema.Description))
                {
                    schema.Description = propertyDescription;
                }

                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(schema.Description))
                schema.Description = typeDescription;

            return Task.CompletedTask;
        }

        private static bool IsReferencedSchema(OpenApiSchema schema) =>
            schema.Metadata is not null &&
            schema.Metadata.TryGetValue(SchemaIdKey, out var id) &&
            id is string { Length: > 0 };
    }
}
