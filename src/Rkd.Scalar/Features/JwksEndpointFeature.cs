using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Rkd.Scalar.Infrastructure;
using Rkd.Scalar.Security.Jwt;

namespace Rkd.Scalar.Features
{
    /// <summary>
    /// Publishes the public signing keys as a JSON Web Key Set, so other services can validate
    /// the tokens issued by this API (for example with <c>JwtOptions.Authority</c> / <c>MetadataAddress</c>).
    /// </summary>
    internal sealed class JwksEndpointFeature : IScalarFeature
    {
        private readonly string _path;

        public JwksEndpointFeature(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
                throw new InvalidOperationException("JWKS path must start with '/'.");

            ReservedRouteGuard.EnsureNotReserved(path);

            _path = path;
        }

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
        }

        public void ConfigureApp(WebApplication app)
        {
            var keys = app.Services.GetService<JwtKeyMaterial>()
                ?? throw new InvalidOperationException(
                    "WithJwksEndpoint requires WithBearerAuth to be configured.");

            var signer = app.Services.GetService<IJwtSigner>();

            var jwks = keys.GetPublicJsonWebKeys(signer?.Algorithm)
                .Select(ToDictionary)
                .ToArray();

            if (jwks.Length == 0)
                throw new InvalidOperationException(
                    "WithJwksEndpoint requires an asymmetric key (PrivateKeyPem, PrivateKeyPath, PublicKeyPem, " +
                    "PublicKeyPath, SigningKey or ValidationKeys). HMAC secrets are never published.");

            var body = new Dictionary<string, object> { ["keys"] = jwks };

            app.MapGet(_path, (HttpContext context) =>
                {
                    context.Response.Headers.CacheControl = "public, max-age=300";
                    return Results.Json(body);
                })
                .AllowAnonymous()
                .ExcludeFromDescription();
        }

        private static Dictionary<string, object> ToDictionary(JsonWebKey key)
        {
            var result = new Dictionary<string, object>();

            void Add(string name, string? value)
            {
                if (!string.IsNullOrEmpty(value))
                    result[name] = value;
            }

            Add("kty", key.Kty);
            Add("use", key.Use);
            Add("kid", key.Kid);
            Add("alg", key.Alg);
            Add("n", key.N);
            Add("e", key.E);
            Add("crv", key.Crv);
            Add("x", key.X);
            Add("y", key.Y);
            Add("x5t", key.X5t);

            if (key.X5c.Count > 0)
                result["x5c"] = key.X5c.ToArray();

            return result;
        }
    }
}
