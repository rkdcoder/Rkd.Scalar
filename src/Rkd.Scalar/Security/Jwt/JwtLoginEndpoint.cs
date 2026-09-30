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
                    IJwtTokenService jwtService,
                    CancellationToken cancellationToken
                ) =>
                {
                    var identity = await validator.ValidateAsync(credential, cancellationToken);

                    if (identity == null)
                        return Results.Unauthorized();

                    var token = await jwtService.GenerateTokenAsync(
                        identity,
                        cancellationToken: cancellationToken);

                    return Results.Ok(JwtLoginResponse.FromResult(token));
                })
            .RequireRateLimiting(rateLimitPolicy)
            .WithTags("Authentication")
            .WithName("RkdScalarJwtLogin")
            .WithSummary("Issues a JWT access token")
            .Produces<JwtLoginResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .AllowAnonymous();
        }
    }
}
