using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi;

namespace Rkd.Scalar.Errors
{
    /// <summary>
    /// Documents the problem responses an operation can actually return, inferred only from facts known about the
    /// endpoint — never guessed (no 404/409/422 unless declared):
    /// 400 when it has parameters or a body, 401/403 when it requires authorization, 429 when it is rate limited,
    /// 500 always. Declared error responses without content get the problem schema.
    /// </summary>
    internal sealed class ErrorResponsesOperationTransformer : IOpenApiOperationTransformer
    {
        private const string MediaType = Rkd.Problems.HttpProblem.MediaType;

        private static readonly BindingSource[] InputSources =
        [
            BindingSource.Path, BindingSource.Query, BindingSource.Header, BindingSource.Body,
            BindingSource.Form, BindingSource.FormFile, BindingSource.ModelBinding, BindingSource.Custom
        ];

        public Task TransformAsync(
            OpenApiOperation operation,
            OpenApiOperationTransformerContext context,
            CancellationToken cancellationToken)
        {
            var description = context.Description;
            var metadata = description.ActionDescriptor.EndpointMetadata;
            var document = context.Document;

            operation.Responses ??= new OpenApiResponses();

            if (description.ParameterDescriptions.Any(p => p.Source is null || InputSources.Contains(p.Source)))
                Add(operation, document, 400, "The parameters or the body are invalid.", validation: true);

            if (RequiresAuthorization(metadata))
            {
                Add(operation, document, 401, "Missing or invalid credentials.");
                Add(operation, document, 403, "Authenticated, but not allowed to perform this operation.");
            }

            if (metadata.OfType<EnableRateLimitingAttribute>().Any() && !metadata.OfType<DisableRateLimitingAttribute>().Any())
                Add(operation, document, 429, "Too many requests; retry after the time in the Retry-After header.");

            Add(operation, document, 500, "Unexpected error. Use the traceId to find it in the logs.");

            // Error responses declared by the endpoint ([ProducesResponseType(404)], .Produces(409)…): without a body they
            // get the problem schema; problem schemas published as JSON/text (MVC's default) move to application/problem+json.
            foreach (var (key, response) in operation.Responses)
            {
                if (response is not OpenApiResponse declared ||
                    !int.TryParse(key, out var status) || status is < 400 or > 599)
                {
                    continue;
                }

                if (declared.Content is null || declared.Content.Count == 0)
                {
                    declared.Content = Content(document, validation: status == 400);
                }
                else if (!declared.Content.ContainsKey(MediaType) && ProblemSchema(declared.Content) is { } schema)
                {
                    declared.Content = new Dictionary<string, OpenApiMediaType> { [MediaType] = new OpenApiMediaType { Schema = schema } };
                }
            }

            return Task.CompletedTask;
        }

        private static void Add(OpenApiOperation operation, OpenApiDocument? document, int status, string explanation, bool validation = false)
        {
            var key = status.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (operation.Responses!.ContainsKey(key))
                return;

            var code = validation ? ProblemCodes.Validation : ProblemCodes.FromStatus(status);

            operation.Responses[key] = new OpenApiResponse
            {
                Description = $"{ReasonPhrases.GetReasonPhrase(status)} — {explanation} (code: {code})",
                Content = Content(document, validation)
            };
        }

        private static Dictionary<string, OpenApiMediaType> Content(OpenApiDocument? document, bool validation) => new()
        {
            [MediaType] = new OpenApiMediaType
            {
                Schema = new OpenApiSchemaReference(
                    validation ? ErrorResponsesDocumentTransformer.ValidationSchemaName : ErrorResponsesDocumentTransformer.ProblemSchemaName,
                    document)
            }
        };

        /// <summary>The problem schema shared by every media type, or <see langword="null"/> when any carries another body.</summary>
        private static IOpenApiSchema? ProblemSchema(IDictionary<string, OpenApiMediaType> content)
        {
            IOpenApiSchema? found = null;

            foreach (var mediaType in content.Values)
            {
                if (mediaType.Schema is not OpenApiSchemaReference reference ||
                    reference.Reference.Id is not ("ProblemDetails" or "HttpValidationProblemDetails" or "ValidationProblemDetails"))
                {
                    return null;
                }

                found ??= reference;
            }

            return found;
        }

        private static bool RequiresAuthorization(IList<object> metadata) =>
            !metadata.OfType<IAllowAnonymous>().Any() &&
            (metadata.OfType<IAuthorizeData>().Any() || metadata.OfType<AuthorizationPolicy>().Any());
    }

    /// <summary>
    /// Registers the problem schemas referenced by <see cref="ErrorResponsesOperationTransformer"/> and documents the
    /// members added by Rkd.Scalar (<c>code</c>, <c>traceId</c>). Reuses ASP.NET Core's schemas when they already exist.
    /// </summary>
    internal sealed class ErrorResponsesDocumentTransformer : IOpenApiDocumentTransformer
    {
        public const string ProblemSchemaName = "ProblemDetails";

        public const string ValidationSchemaName = "HttpValidationProblemDetails";

        public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
        {
            document.Components ??= new OpenApiComponents();
            document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();

            Ensure(document.Components.Schemas, ProblemSchemaName, validation: false);
            Ensure(document.Components.Schemas, ValidationSchemaName, validation: true);

            return Task.CompletedTask;
        }

        private static void Ensure(IDictionary<string, IOpenApiSchema> schemas, string name, bool validation)
        {
            if (!schemas.TryGetValue(name, out var existing))
            {
                schemas[name] = Create(validation);
                return;
            }

            if (existing is OpenApiSchema schema)
            {
                schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
                schema.Properties.TryAdd(ProblemCodes.ExtensionName, Code());
                schema.Properties.TryAdd(ProblemCodes.TraceIdName, TraceId());
            }
        }

        private static OpenApiSchema Create(bool validation)
        {
            var properties = new Dictionary<string, IOpenApiSchema>
            {
                ["type"] = Text("URI reference that identifies the problem type."),
                ["title"] = Text("Short, human-readable summary of the problem type."),
                ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer | JsonSchemaType.Null, Format = "int32", Description = "HTTP status code." },
                ["detail"] = Text("Explanation specific to this occurrence of the problem."),
                ["instance"] = Text("Request path where the problem occurred."),
                [ProblemCodes.ExtensionName] = Code(),
                [ProblemCodes.TraceIdName] = TraceId()
            };

            if (validation)
            {
                properties[ProblemCodes.ErrorsName] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Description = "Validation messages by field.",
                    AdditionalProperties = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Array,
                        Items = new OpenApiSchema { Type = JsonSchemaType.String }
                    }
                };
            }

            return new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description = validation
                    ? "RFC 9457 validation problem (application/problem+json)."
                    : "RFC 9457 problem details (application/problem+json).",
                Properties = properties
            };
        }

        private static OpenApiSchema Text(string description) =>
            new() { Type = JsonSchemaType.String | JsonSchemaType.Null, Description = description };

        private static OpenApiSchema Code() =>
            new() { Type = JsonSchemaType.String, Description = "Stable, machine-readable error code (e.g. CUSTOMER_NOT_FOUND, VALIDATION_ERROR). Switch on it instead of the message." };

        private static OpenApiSchema TraceId() =>
            new() { Type = JsonSchemaType.String, Description = "Trace identifier, the same written to the logs." };
    }
}
