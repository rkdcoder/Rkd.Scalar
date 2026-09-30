using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Scalar.AspNetCore;

namespace Rkd.Scalar.Infrastructure
{
    /// <summary>
    /// Builds the list of documents shown in the Scalar version selector and keeps the selector visible
    /// when a version is part of the URL.
    /// </summary>
    /// <remarks>
    /// Scalar filters the page down to a single document when the URL names one (<c>/scalar/v1</c>),
    /// which hides the version dropdown. Those URLs are redirected to <c>/scalar/?document=v1</c>, which
    /// lists every version and preselects the requested one.
    /// </remarks>
    internal static class ScalarDocumentSelector
    {
        public const string DocumentQueryParameter = "document";

        private const string DefaultDocument = "v1";

        internal sealed record Document(string Name, string Title, bool IsDeprecated);

        /// <summary>
        /// Every API version, newest first; deprecated versions are flagged in the title.
        /// </summary>
        public static IReadOnlyList<Document> GetDocuments(IServiceProvider services)
        {
            var provider = services.GetService<IApiVersionDescriptionProvider>();

            if (provider is null)
                return [new Document(DefaultDocument, DefaultDocument, false)];

            var deprecatedByEndpoints = GetVersionsDeprecatedByEveryEndpoint(services);
            var now = DateTimeOffset.UtcNow;

            var documents = provider.ApiVersionDescriptions
                .OrderByDescending(d => d.ApiVersion)
                .Select(d =>
                {
                    var deprecated =
                        d.IsDeprecated ||
                        d.DeprecationPolicy?.IsEffective(now) == true ||
                        deprecatedByEndpoints.Contains(d.ApiVersion);

                    return new Document(d.GroupName, deprecated ? $"{d.GroupName} (deprecated)" : d.GroupName, deprecated);
                })
                .DistinctBy(d => d.Name)
                .ToList();

            return documents.Count > 0
                ? documents
                : [new Document(DefaultDocument, DefaultDocument, false)];
        }

        /// <summary>
        /// API versions that every endpoint declaring them marks as deprecated
        /// (<c>[ApiVersion("1.0", Deprecated = true)]</c>, <c>HasDeprecatedApiVersion</c>).
        /// </summary>
        /// <remarks>
        /// Asp.Versioning 10 only flags <see cref="ApiVersionDescription.IsDeprecated"/> through deprecation
        /// policies, so the classic attribute-based deprecation is read from the endpoint metadata.
        /// </remarks>
        private static HashSet<ApiVersion> GetVersionsDeprecatedByEveryEndpoint(IServiceProvider services)
        {
            var declared = new HashSet<ApiVersion>();
            var supported = new HashSet<ApiVersion>();
            var deprecated = new HashSet<ApiVersion>();

            var endpoints = services.GetService<EndpointDataSource>()?.Endpoints ?? [];

            foreach (var endpoint in endpoints)
            {
                if (endpoint.Metadata.GetMetadata<ApiVersionMetadata>() is not { IsApiVersionNeutral: false } metadata)
                    continue;

                var model = metadata.Map(ApiVersionMapping.Explicit | ApiVersionMapping.Implicit);

                declared.UnionWith(model.SupportedApiVersions);
                declared.UnionWith(model.DeprecatedApiVersions);
                supported.UnionWith(model.SupportedApiVersions);
                deprecated.UnionWith(model.DeprecatedApiVersions);
            }

            deprecated.ExceptWith(supported);
            deprecated.IntersectWith(declared);

            return deprecated;
        }

        /// <summary>
        /// Adds every document to Scalar, preselecting the one requested in the query string or,
        /// by default, the newest non-deprecated version.
        /// </summary>
        public static void AddDocuments(ScalarOptions options, HttpContext context)
        {
            var documents = GetDocuments(context.RequestServices);

            var requested = context.Request.Query[DocumentQueryParameter].ToString();

            var selected =
                documents.FirstOrDefault(d => string.Equals(d.Name, requested, StringComparison.OrdinalIgnoreCase))?.Name ??
                documents.FirstOrDefault(d => !d.IsDeprecated)?.Name ??
                documents[0].Name;

            foreach (var document in documents)
            {
                options.AddDocument(document.Name, document.Title, isDefault: document.Name == selected);
            }
        }

        /// <summary>
        /// Redirects <c>{prefix}/{version}</c> to <c>{prefix}/?document={version}</c> when the API has more
        /// than one version, so the Scalar version dropdown is always available.
        /// </summary>
        public static void UseVersionRedirect(WebApplication app, string scalarRoutePrefix)
        {
            var prefix = "/" + scalarRoutePrefix.Trim().Trim('/');

            app.Use(async (context, next) =>
            {
                if (TryGetRequestedDocument(context, prefix, out var document))
                {
                    context.Response.Redirect(
                        $"{context.Request.PathBase}{prefix}/?{DocumentQueryParameter}={Uri.EscapeDataString(document)}");
                    return;
                }

                await next(context);
            });
        }

        private static bool TryGetRequestedDocument(HttpContext context, string prefix, out string document)
        {
            document = string.Empty;

            var path = context.Request.Path.Value;

            if (!HttpMethods.IsGet(context.Request.Method) ||
                path is null ||
                !path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                return false;

            var segment = path[(prefix.Length + 1)..].TrimEnd('/');

            if (segment.Length == 0 || segment.Contains('/') || segment.Contains('.'))
                return false;

            var documents = GetDocuments(context.RequestServices);

            if (documents.Count < 2)
                return false;

            var match = documents.FirstOrDefault(d => string.Equals(d.Name, segment, StringComparison.OrdinalIgnoreCase));

            if (match is null)
                return false;

            document = match.Name;
            return true;
        }
    }
}
