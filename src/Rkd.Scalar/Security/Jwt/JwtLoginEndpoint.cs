using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Rkd.Scalar.Security.Contracts;

namespace Rkd.Scalar.Security.Jwt
{
    internal static class JwtLoginEndpoint
    {
        public static void MapJwtLogin<TCredential>(
            WebApplication app,
            string path)
            where TCredential : class
        {
            app.MapPost(path,
                async (
                    JwtLoginRequest request,
                    ICredentialValidator<TCredential> validator,
                    IJwtTokenService jwtService
                ) =>
                {
                    var credential = Activator.CreateInstance(
                        typeof(TCredential),
                        request.Username,
                        request.Password) as TCredential;

                    if (credential == null)
                        return Results.BadRequest();

                    var identity = await validator.ValidateAsync(credential);

                    if (identity == null)
                        return Results.Unauthorized();

                    var token = jwtService.GenerateToken(identity);

                    return Results.Ok(new
                    {
                        access_token = token.Token,
                        expires_at = token.ExpiresAtUtc
                    });
                })
            .WithTags("Authentication")
            .AllowAnonymous();
        }
    }
}
