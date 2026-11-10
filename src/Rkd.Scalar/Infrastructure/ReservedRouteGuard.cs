using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Rkd.Scalar.Infrastructure
{
    internal static class ReservedRouteGuard
    {
        private static readonly string[] ReservedPrefixes =
        [
            "/scalar",
            "/openapi"
        ];

        public static void EnsureNotReserved(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Path cannot be empty.");

            foreach (var reserved in ReservedPrefixes)
            {
                if (path.StartsWith(reserved, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"The path '{path}' is reserved by Rkd.Scalar. " +
                        $"Do not create endpoints under '{reserved}'.");
                }
            }
        }

        /// <summary>Metadata of the endpoints of the documentation itself (OpenAPI documents, Scalar UI).</summary>
        internal sealed class DocumentationEndpoint
        {
            public static readonly DocumentationEndpoint Instance = new();

            private DocumentationEndpoint()
            {
            }
        }

        /// <summary>The paths the documentation uses: the Scalar prefix and the fixed start of the OpenAPI pattern.</summary>
        public static IEnumerable<string> DocumentationPaths(RkdScalarOptions options)
        {
            if (FixedStart(options.ScalarRoutePrefix) is { } scalar)
                yield return scalar;

            if (FixedStart(options.OpenApiRoutePattern) is { } openApi)
                yield return openApi;
        }

        /// <summary>
        /// The literal segments before the first parameter: <c>/openapi/{documentName}.json</c> is <c>/openapi</c>;
        /// <see langword="null"/> when the pattern starts with a parameter (nothing fixed to reserve).
        /// </summary>
        internal static string? FixedStart(string? pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return null;

            var segments = pattern.Trim().TrimStart('~').Trim('/').Split('/')
                .TakeWhile(segment => segment.Length > 0 && !segment.Contains('{'))
                .ToList();

            return segments.Count == 0 ? null : "/" + string.Join('/', segments);
        }

        /// <summary>
        /// Logs a warning for each endpoint of the application under <paramref name="documentationPaths"/>. Runs when the
        /// pipeline is built, when every endpoint is known (in UseRkdScalar the endpoints of a WebApplication are not
        /// registered yet). Never throws: those endpoints worked until now.
        /// </summary>
        public static void WarnAboutEndpointsUnderTheDocumentation(IServiceProvider services, IReadOnlyCollection<string> documentationPaths)
        {
            if (documentationPaths.Count == 0 || services.GetService<EndpointDataSource>() is not { } dataSource)
                return;

            IReadOnlyList<Endpoint> endpoints;

            try
            {
                endpoints = dataSource.Endpoints;
            }
            catch (Exception)
            {
                return;   // the route table itself failed: RouteTemplateValidation or ASP.NET Core report it
            }

            ILogger? logger = null;

            foreach (var endpoint in endpoints)
            {
                if (endpoint is not RouteEndpoint route || endpoint.Metadata.GetMetadata<DocumentationEndpoint>() is not null)
                    continue;

                var path = "/" + (route.RoutePattern.RawText ?? string.Empty).Trim().TrimStart('~').Trim('/');

                if (documentationPaths.FirstOrDefault(reserved => IsUnder(path, reserved)) is not { } reservedPath)
                    continue;

                logger ??= services.GetService<ILoggerFactory>()?.CreateLogger(RouteTemplateValidation.LoggerCategory);
                logger?.LogWarning(
                    "Rkd.Scalar: the endpoint '{Endpoint}' ({Path}) is under '{Reserved}', where Rkd.Scalar serves the API documentation. " +
                    "Requests there may reach the documentation instead of the endpoint, or the other way round: move the endpoint, " +
                    "or the documentation (RkdScalar:ScalarRoutePrefix, RkdScalar:OpenApiRoutePattern).",
                    endpoint.DisplayName, path, reservedPath);
            }
        }

        /// <summary>Whole segments: <c>/scalar/admin</c> is under <c>/scalar</c>, <c>/scalars</c> is not.</summary>
        private static bool IsUnder(string path, string prefix) =>
            path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }
}
