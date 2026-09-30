using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Infrastructure;
using Rkd.Scalar.Security.Jwt;
using System.Globalization;
using System.Threading.RateLimiting;

namespace Rkd.Scalar.Features
{
    internal sealed class JwtLoginEndpointFeature<TCredentials> : IScalarFeature
        where TCredentials : class
    {
        private readonly string _path;

        private readonly string? _rateLimitPolicy;

        private readonly int? _permitLimit;

        private readonly TimeSpan? _window;

        private const string DefaultPolicyName = "rkd-scalar-login";

        public JwtLoginEndpointFeature(
            string path,
            string rateLimitPolicy)
        {
            ValidatePath(path);

            if (string.IsNullOrWhiteSpace(rateLimitPolicy))
                throw new InvalidOperationException(
                    "A rate limiting policy name is required for WithJwtLoginEndpoint.");

            _path = path;
            _rateLimitPolicy = rateLimitPolicy;
        }

        public JwtLoginEndpointFeature(
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
                var retryAfterSeconds =
                    ((int)Math.Ceiling(window.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                services.AddRateLimiter(options =>
                {
                    // Partitioned per client IP: one abusive client cannot lock everybody else out of the login.
                    options.AddPolicy(DefaultPolicyName, context =>
                        RateLimitPartition.GetFixedWindowLimiter(
                            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                            _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = permitLimit,
                                Window = window,
                                QueueLimit = 0
                            }));

                    options.OnRejected ??= (context, token) =>
                    {
                        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                        context.HttpContext.Response.Headers.RetryAfter =
                            context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                                ? ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture)
                                : retryAfterSeconds;

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
                    "WithJwtLoginEndpoint requires JWT authentication with a credential validator: " +
                    "call WithBearerAuth<TCredentials, TValidator>(). " +
                    "The non-generic WithBearerAuth overloads are validation-only and cannot issue tokens.");
            }

            if (bearerFeature.CredentialType != typeof(TCredentials))
            {
                throw new InvalidOperationException(
                    $"WithJwtLoginEndpoint<{typeof(TCredentials).Name}> must use the same credential type configured in " +
                    $"WithBearerAuth<{bearerFeature.CredentialType.Name}, ...>.");
            }

            JwtLoginEndpoint.Map<TCredentials>(
                app,
                _path,
                _rateLimitPolicy ?? DefaultPolicyName);
        }

        private static void ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/"))
                throw new InvalidOperationException("Login path must start with '/'.");

            if (path.Contains(" "))
                throw new InvalidOperationException("Login path cannot contain spaces.");

            ReservedRouteGuard.EnsureNotReserved(path);
        }
    }
}