using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

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

        public static void EnsureControllersDoNotUseReservedRoutes(WebApplication app)
        {
            var endpoints = app.Services
                .GetRequiredService<EndpointDataSource>()
                .Endpoints;

            foreach (var endpoint in endpoints)
            {
                var route = endpoint.DisplayName;

                if (string.IsNullOrWhiteSpace(route))
                    continue;

                foreach (var reserved in ReservedPrefixes)
                {
                    if (route.Contains(reserved, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"A controller endpoint is using reserved route '{reserved}'. " +
                            $"Routes starting with '/scalar' or '/openapi' are reserved by Rkd.Scalar.");
                    }
                }
            }
        }
    }
}
