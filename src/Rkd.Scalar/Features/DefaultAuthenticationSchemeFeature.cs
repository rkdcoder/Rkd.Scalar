using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rkd.Scalar.Infrastructure;
using Rkd.Scalar.Security.ApiKey;

namespace Rkd.Scalar.Features
{
    /// <summary>
    /// Registers a policy scheme that forwards each request to the Rkd.Scalar scheme matching the
    /// credentials it carries, and makes it the default scheme so a plain <c>[Authorize]</c> works
    /// with Bearer, Basic and API Key at the same time.
    /// </summary>
    internal sealed class DefaultAuthenticationSchemeFeature : IScalarFeature
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = RkdScalarAuthenticationSchemes.Default;
            });

            services.AddAuthentication()
                .AddPolicyScheme(
                    RkdScalarAuthenticationSchemes.Default,
                    "Rkd.Scalar",
                    options => options.ForwardDefaultSelector = SelectScheme);
        }

        public void ConfigureApp(WebApplication app)
        {
            var registry = app.Services.GetRequiredService<ScalarFeatureRegistry>();

            if (registry.AuthenticationSchemes.Count == 0)
                throw new InvalidOperationException(
                    "WithDefaultAuthenticationScheme requires at least one of WithBearerAuth, WithBasicAuth or WithApiKeyAuth.");
        }

        internal static string? SelectScheme(HttpContext context)
        {
            var schemes = context.RequestServices
                .GetRequiredService<ScalarFeatureRegistry>()
                .AuthenticationSchemes;

            var authorization = context.Request.Headers.Authorization.ToString();

            if (schemes.Contains(RkdScalarAuthenticationSchemes.Bearer) &&
                authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return RkdScalarAuthenticationSchemes.Bearer;

            if (schemes.Contains(RkdScalarAuthenticationSchemes.Basic) &&
                authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return RkdScalarAuthenticationSchemes.Basic;

            if (schemes.Contains(RkdScalarAuthenticationSchemes.ApiKey))
            {
                var headerName = context.RequestServices
                    .GetRequiredService<IOptionsMonitor<ApiKeyAuthenticationOptions>>()
                    .Get(RkdScalarAuthenticationSchemes.ApiKey)
                    .HeaderName;

                if (context.Request.Headers.ContainsKey(headerName))
                    return RkdScalarAuthenticationSchemes.ApiKey;
            }

            // No credentials: challenge/forbid with the first registered scheme.
            return schemes.FirstOrDefault();
        }
    }
}
