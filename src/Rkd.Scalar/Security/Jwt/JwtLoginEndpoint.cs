using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Rkd.Scalar.Security.Jwt
{
    internal static class JwtLoginEndpoint
    {
        public static void Map<TCredentials>(
            WebApplication app,
            string path,
            string rateLimitPolicy)
            where TCredentials : class
        {
            app.MapPost(path,
                async (
                    TCredentials credentials,
                    ICredentialValidator<TCredentials> validator,
                    IJwtTokenService tokens,
                    CancellationToken cancellationToken
                ) =>
                {
                    var result = await validator.ValidateAsync(credentials, cancellationToken);

                    if (result is not { Succeeded: true })
                        return Results.Problem(
                            statusCode: StatusCodes.Status401Unauthorized,
                            title: "Invalid credentials",
                            detail: result?.FailureReason);

                    var token = await tokens.CreateTokenAsync(
                        result.Identity,
                        cancellationToken: cancellationToken);

                    return Results.Ok(JwtLoginResponse.FromToken(token));
                })
            .RequireRateLimiting(rateLimitPolicy)
            .WithTags("Authentication")
            .WithName("RkdScalarJwtLogin")
            .WithSummary("Issues a JWT access token")
            .Produces<JwtLoginResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .AllowAnonymous();
        }
    }
}
