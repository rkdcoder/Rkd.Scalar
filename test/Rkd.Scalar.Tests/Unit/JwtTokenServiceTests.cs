using System.Security.Claims;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Rkd.Scalar.Security.Jwt;
using System.IdentityModel.Tokens.Jwt;

namespace Rkd.Scalar.Tests.Unit
{
    public class JwtTokenServiceTests
    {
        private readonly JwtOptions _options = new()
        {
            Secret = new string('a', 32), // 32 chars min
            Issuer = "test-issuer",
            Audience = "test-audience",
            Expiration = TimeSpan.FromHours(1),
            ValidateNotBefore = false
        };

        [Fact]
        public void GenerateToken_ShouldCreateValidJwt()
        {
            var service = new JwtTokenService(_options);
            var identity = new ClaimsIdentity([new Claim("sub", "123")]);

            var result = service.GenerateToken(identity);

            result.Token.Should().NotBeNullOrEmpty();

            // Validar leitura do token
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(result.Token);

            token.Issuer.Should().Be(_options.Issuer);
            token.Audiences.Should().Contain(_options.Audience);
            token.Claims.First(c => c.Type == "sub").Value.Should().Be("123");
        }
    }
}
