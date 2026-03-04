using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Rkd.Scalar.Security.Jwt
{
    public sealed class JwtTokenService : IJwtTokenService
    {
        private readonly JwtOptions _options;

        public JwtTokenService(JwtOptions options)
        {
            _options = options;
        }

        public JwtTokenResult GenerateToken(
            ClaimsIdentity identity,
            IEnumerable<Claim>? additionalClaims = null)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_options.Secret));

            var now = DateTime.UtcNow;

            var claims = identity.Claims.ToList();

            if (additionalClaims != null)
                claims.AddRange(additionalClaims);

            var signingCredentials = new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                notBefore: _options.ValidateNotBefore ? now : null,
                expires: now.Add(_options.Expiration),
                signingCredentials: signingCredentials
            );

            var handler = new JwtSecurityTokenHandler();

            var tokenString = handler.WriteToken(token);

            return new JwtTokenResult
            {
                Token = tokenString,
                ExpiresAtUtc = token.ValidTo
            };
        }
    }
}
