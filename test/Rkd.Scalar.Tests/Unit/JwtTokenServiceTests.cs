using FluentAssertions;
using Rkd.Scalar.Tests.Helpers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace Rkd.Scalar.Tests.Unit
{
    public class JwtTokenServiceTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static ClaimsIdentity Identity() => new(
        [
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(ClaimTypes.Name, "rodrigo"),
            new Claim(ClaimTypes.Role, "admin"),
            new Claim(ClaimTypes.Role, "reader"),
            new Claim(ClaimTypes.Email, "r@example.com"),
            new Claim("tenant", "acme"),
            new Claim("level", "7", ClaimValueTypes.Integer),
            new Claim("aud", "attacker")
        ]);

        private static JsonElement Payload(string token) =>
            JsonDocument.Parse(Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Decode(token.Split('.')[1])).RootElement;

        [Fact]
        public async Task CreateTokenAsync_ShouldUseStandardClaimNames_AndRegisteredClaims()
        {
            var options = TestJwt.Options(o => { o.Secret = new string('a', 32); o.ExpirationInMinutes = 30; });

            var token = await TestJwt.Service(options).CreateTokenAsync(Identity(), cancellationToken: Ct);
            var payload = Payload(token.AccessToken);

            payload.GetProperty("sub").GetString().Should().Be("42");
            payload.GetProperty("name").GetString().Should().Be("rodrigo");
            payload.GetProperty("email").GetString().Should().Be("r@example.com");
            payload.GetProperty("role").EnumerateArray().Select(r => r.GetString()).Should().Equal("admin", "reader");
            payload.GetProperty("tenant").GetString().Should().Be("acme");
            payload.GetProperty("level").GetInt32().Should().Be(7);

            payload.GetProperty("iss").GetString().Should().Be("issuer");
            payload.GetProperty("aud").GetString().Should().Be("audience", "identity claims never override registered claims");
            payload.GetProperty("jti").GetString().Should().Be(token.TokenId);
            payload.GetProperty("iat").GetInt64().Should().Be(token.IssuedAt.ToUnixTimeSeconds());
            payload.GetProperty("nbf").GetInt64().Should().Be(token.IssuedAt.ToUnixTimeSeconds());
            payload.GetProperty("exp").GetInt64().Should().Be(token.ExpiresAt.ToUnixTimeSeconds());

            (token.ExpiresAt - token.IssuedAt).Should().Be(TimeSpan.FromMinutes(30));
            token.AccessToken.Should().NotContain("schemas.xmlsoap.org");
        }

        [Fact]
        public async Task CreateTokenAsync_WithoutStandardNames_ShouldKeepDotNetClaimTypes()
        {
            var options = TestJwt.Options(o => { o.Secret = new string('a', 32); o.UseStandardClaimNames = false; });

            var token = await TestJwt.Service(options).CreateTokenAsync(Identity(), cancellationToken: Ct);

            Payload(token.AccessToken).TryGetProperty(ClaimTypes.Name, out _).Should().BeTrue();
        }

        [Fact]
        public async Task CreateTokenAsync_ShouldGenerateUniqueTokenIds_AndHonorProvidedJti()
        {
            var service = TestJwt.Service(TestJwt.Options(o => o.Secret = new string('a', 32)));

            var first = await service.CreateTokenAsync(new ClaimsIdentity(), cancellationToken: Ct);
            var second = await service.CreateTokenAsync(new ClaimsIdentity(), cancellationToken: Ct);
            var provided = await service.CreateTokenAsync(new ClaimsIdentity([new Claim("jti", "fixed-id")]), cancellationToken: Ct);

            first.TokenId.Should().NotBe(second.TokenId);
            provided.TokenId.Should().Be("fixed-id");
        }

        [Fact]
        public async Task CreateTokenAsync_WithHmac_ShouldUseHs256WithoutKid()
        {
            var token = await TestJwt.Service(TestJwt.Options(o => o.Secret = new string('a', 32)))
                .CreateTokenAsync(new ClaimsIdentity(), cancellationToken: Ct);

            var header = new JwtSecurityTokenHandler().ReadJwtToken(token.AccessToken).Header;
            header.Alg.Should().Be("HS256");
            header.Kid.Should().BeNull();
        }
    }
}
