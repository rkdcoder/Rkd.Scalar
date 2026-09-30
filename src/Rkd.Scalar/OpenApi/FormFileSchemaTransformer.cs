using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Rkd.Scalar.OpenApi
{
    /// <summary>
    /// Writes <c>IFormFile</c> / <c>IFormFileCollection</c> as inline <c>{ "type": "string", "format": "binary" }</c>.
    /// ASP.NET Core 10 references them as components (<c>$ref: IFormFile</c>), which Scalar and other tools do not
    /// render as file inputs. Form models with files are also offered as <c>multipart/form-data</c> (MVC only
    /// declares <c>application/x-www-form-urlencoded</c> for <c>[FromForm]</c> models, which cannot carry files).
    /// </summary>
    internal sealed class FormFileSchemaTransformer : IOpenApiDocumentTransformer
    {
        private const string FormFile = "IFormFile";

        private const string FormFileCollection = "IFormFileCollection";

        private const string Multipart = "multipart/form-data";

        private const string UrlEncoded = "application/x-www-form-urlencoded";

        public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
        {
            var schemas = document.Components?.Schemas;

            if (schemas is null || (!schemas.ContainsKey(FormFile) && !schemas.ContainsKey(FormFileCollection)))
                return Task.CompletedTask;

            // Components (form models) that contain files, directly or through other components.
            var withFiles = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (name, schema) in schemas.ToList())
            {
                if (name is FormFile or FormFileCollection)
                    continue;

                schemas[name] = Replace(schema, new HashSet<IOpenApiSchema>(ReferenceEqualityComparer.Instance), out var hasFiles);

                if (hasFiles)
                    withFiles.Add(name);
            }

            foreach (var operation in document.Paths?.Values.SelectMany(p => p.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>()) ?? [])
            {
                if (operation.RequestBody?.Content is { } content)
                    TransformContent(content, withFiles);

                foreach (var response in operation.Responses?.Values ?? Enumerable.Empty<IOpenApiResponse>())
                {
                    if (response.Content is { } responseContent)
                        TransformContent(responseContent, withFiles);
                }
            }

            schemas.Remove(FormFile);
            schemas.Remove(FormFileCollection);

            return Task.CompletedTask;
        }

        private static void TransformContent(IDictionary<string, OpenApiMediaType> content, HashSet<string> withFiles)
        {
            var hasFiles = false;

            foreach (var mediaType in content.Values)
            {
                if (mediaType.Schema is null)
                    continue;

                mediaType.Schema = Replace(mediaType.Schema, new HashSet<IOpenApiSchema>(ReferenceEqualityComparer.Instance), out var replaced);

                hasFiles |= replaced ||
                            (mediaType.Schema is OpenApiSchemaReference reference && reference.Reference.Id is { } id && withFiles.Contains(id));
            }

            if (hasFiles && !content.ContainsKey(Multipart) && content.TryGetValue(UrlEncoded, out var form))
            {
                content.Remove(UrlEncoded);
                content[Multipart] = form;
            }
        }

        /// <summary>Returns the schema with every file reference inlined; <paramref name="hasFiles"/> tells whether any was found.</summary>
        private static IOpenApiSchema Replace(IOpenApiSchema schema, HashSet<IOpenApiSchema> visited, out bool hasFiles)
        {
            hasFiles = false;

            if (schema is OpenApiSchemaReference reference)
            {
                switch (reference.Reference.Id)
                {
                    case FormFile:
                        hasFiles = true;
                        return Binary();
                    case FormFileCollection:
                        hasFiles = true;
                        return new OpenApiSchema { Type = JsonSchemaType.Array, Items = Binary() };
                    default:
                        return schema;
                }
            }

            if (schema is not OpenApiSchema target || !visited.Add(target))
                return schema;

            var found = false;

            if (target.Properties is { } properties)
            {
                foreach (var name in properties.Keys.ToList())
                {
                    properties[name] = Replace(properties[name], visited, out var property);
                    found |= property;
                }
            }

            if (target.Items is { } items)
            {
                target.Items = Replace(items, visited, out var item);
                found |= item;
            }

            if (target.AdditionalProperties is { } additional)
            {
                target.AdditionalProperties = Replace(additional, visited, out var value);
                found |= value;
            }

            found |= ReplaceAll(target.AllOf, visited) | ReplaceAll(target.AnyOf, visited) | ReplaceAll(target.OneOf, visited);

            hasFiles = found;

            // "oneOf": [{ "type": "null" }, <file>] (nullable IFormFile?) becomes a plain file field.
            if (target.OneOf is { Count: > 0 } oneOf && oneOf.Any(IsBinary) &&
                oneOf.All(s => IsBinary(s) || s is OpenApiSchema { Type: JsonSchemaType.Null }))
            {
                return Binary();
            }

            return target;
        }

        private static bool ReplaceAll(IList<IOpenApiSchema>? schemas, HashSet<IOpenApiSchema> visited)
        {
            var found = false;

            for (var i = 0; schemas is not null && i < schemas.Count; i++)
            {
                schemas[i] = Replace(schemas[i], visited, out var replaced);
                found |= replaced;
            }

            return found;
        }

        private static bool IsBinary(IOpenApiSchema schema) =>
            schema is OpenApiSchema { Format: "binary" } binary && binary.Type?.HasFlag(JsonSchemaType.String) == true;

        private static OpenApiSchema Binary() => new() { Type = JsonSchemaType.String, Format = "binary" };
    }
}
