using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Rkd.Scalar.Security.Contracts;

namespace Rkd.Scalar.Security.Jwt
{
    internal static class JwtLoginEndpoint
    {
        public static void MapJwtLogin<TCredential>(
            WebApplication app,
            string path,
            string rateLimitPolicy)
            where TCredential : class
        {
            app.MapPost(path,
                async (
                    TCredential credential,
                    ICredentialValidator<TCredential> validator,
                    IJwtTokenService jwtService
                ) =>
                {
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
            .RequireRateLimiting(rateLimitPolicy)
            .WithTags("Authentication")
            .AllowAnonymous();
        }
    }
}
