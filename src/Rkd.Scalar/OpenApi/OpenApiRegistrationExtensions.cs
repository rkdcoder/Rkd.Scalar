using Microsoft.Extensions.DependencyInjection;

namespace Rkd.Scalar.OpenApi
{
    internal static class OpenApiRegistrationExtensions
    {
        public static IServiceCollection RegisterOpenApiDocument(
            this IServiceCollection services,
            string name)
        {
            services.PostConfigure<OpenApiDocumentRegistry>(registry =>
            {
                registry.Register(name);
            });

            return services;
        }
    }
}
