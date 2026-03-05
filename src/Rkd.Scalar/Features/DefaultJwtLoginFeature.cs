using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Infrastructure;
using Rkd.Scalar.Security.Jwt;
using System.Threading.RateLimiting;


namespace Rkd.Scalar.Features
{
    internal sealed class DefaultJwtLoginFeature<TCredential> : IScalarFeature
        where TCredential : class
    {
        private readonly string _path;

        private readonly string? _rateLimitPolicy;

        private readonly int? _permitLimit;

        private readonly TimeSpan? _window;

        private const string DefaultPolicyName = "rkd-scalar-login";

        public DefaultJwtLoginFeature(
            string path,
            string rateLimitPolicy)
        {
            ValidatePath(path);

            if (string.IsNullOrWhiteSpace(rateLimitPolicy))
                throw new InvalidOperationException(
                    "RateLimitPolicy is required for WithDefaultJwtLogin.");

            _path = path;
            _rateLimitPolicy = rateLimitPolicy;
        }

        public DefaultJwtLoginFeature(
            string path,
            int permitLimit,
            TimeSpan window)
        {
            ValidatePath(path);

            if (permitLimit <= 0)
                throw new InvalidOperationException(
                    "PermitLimit must be greater than zero.");

            if (window <= TimeSpan.Zero)
                throw new InvalidOperationException(
                    "Window must be greater than zero.");

            _path = path;
            _permitLimit = permitLimit;
            _window = window;
        }

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            if (_permitLimit is int permitLimit && _window is TimeSpan window)
            {
                services.AddRateLimiter(options =>
                {
                    options.AddFixedWindowLimiter("rkd-scalar-login", opt =>
                    {
                        opt.PermitLimit = 5;
                        opt.Window = TimeSpan.FromMinutes(1);
                        opt.QueueLimit = 0;
                    });

                    options.OnRejected = (context, token) =>
                    {
                        context.HttpContext.Response.StatusCode = 429;
                        context.HttpContext.Response.Headers.RetryAfter = "60";
                        return ValueTask.CompletedTask;
                    };
                });
            }
        }

        public void ConfigureApp(WebApplication app)
        {
            var registry = app.Services.GetRequiredService<ScalarFeatureRegistry>();

            var bearerFeature = registry.Features
                .OfType<IBearerAuthFeature>()
                .FirstOrDefault();

            if (bearerFeature == null)
            {
                throw new InvalidOperationException(
                    "WithDefaultJwtLogin requires JWT authentication. " +
                    "Call WithBearerAuth<TCredential, TValidator>() before calling WithDefaultJwtLogin().");
            }

            if (bearerFeature.CredentialType != typeof(TCredential))
            {
                throw new InvalidOperationException(
                    $"WithDefaultJwtLogin<{typeof(TCredential).Name}> must use the same credential type configured in WithBearerAuth.");
            }

            JwtLoginEndpoint.MapJwtLogin<TCredential>(
                app,
                _path,
                "rkd-scalar-login");
        }

        private static void ValidatePath(string path)
        {
            if (!path.StartsWith("/"))
                throw new InvalidOperationException("Login path must start with '/'.");

            if (path.Contains(" "))
                throw new InvalidOperationException("Login path cannot contain spaces.");

            ReservedRouteGuard.EnsureNotReserved(path);
        }
    }
}
