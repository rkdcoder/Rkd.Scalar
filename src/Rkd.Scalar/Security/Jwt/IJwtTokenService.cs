using System.Security.Claims;

namespace Rkd.Scalar.Security.Jwt
{
    public interface IJwtTokenService
    {
        JwtTokenResult GenerateToken(
            ClaimsIdentity identity,
            IEnumerable<Claim>? additionalClaims = null);
    }
}
