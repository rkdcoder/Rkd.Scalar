using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Security.Jwt;


namespace Rkd.Scalar.Features
{
    internal sealed class DefaultJwtLoginFeature<TCredential> : IScalarFeature
        where TCredential : class
    {
        private readonly string _path;

        public DefaultJwtLoginFeature(string path)
        {
            ValidatePath(path);
            _path = path;
        }

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            var bearerFeature = services
                .FirstOrDefault(s => s.ImplementationInstance is IBearerAuthFeature)
                ?.ImplementationInstance as IBearerAuthFeature;

            if (bearerFeature == null)
            {
                throw new InvalidOperationException(
                    "WithDefaultJwtLogin requires JWT authentication. " +
                    "Call WithBearerAuth<TCredential, TValidator>() before calling WithDefaultJwtLogin().");
            }

            if (bearerFeature.CredentialType != typeof(TCredential))
            {
                throw new InvalidOperationException(
                    $"WithDefaultJwtLogin<{typeof(TCredential).Name}> must use the same credential type configured in WithBearerAuth. " +
                    $"Expected: {bearerFeature.CredentialType.Name}. " +
                    $"Received: {typeof(TCredential).Name}.");
            }
        }

        public void ConfigureApp(WebApplication app)
        {
            JwtLoginEndpoint.MapJwtLogin<TCredential>(app, _path);
        }

        private static void ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Login path cannot be empty.");

            if (!path.StartsWith("/"))
                throw new InvalidOperationException("Login path must start with '/'.");

            if (path.Contains(" "))
                throw new InvalidOperationException("Login path cannot contain spaces.");
        }
    }
}
