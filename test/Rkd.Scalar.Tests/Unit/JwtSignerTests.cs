using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Rkd.Scalar.Security.Jwt;
using Rkd.Scalar.Tests.Helpers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace Rkd.Scalar.Tests.Unit
{
    public sealed record FakeKmsKey(string PrivatePem);

    /// <summary>Simulates a remote key store (KMS / Key Vault) that signs asynchronously.</summary>
    public sealed class FakeKmsSigner : IJwtSigner
    {
        private readonly RSA _rsa;

        public FakeKmsSigner(FakeKmsKey key)
        {
            _rsa = RSA.Create();
            _rsa.ImportFromPem(key.PrivatePem);
        }

        public int Calls { get; private set; }

        public string Algorithm => SecurityAlgorithms.RsaSha256;

        public string? KeyId => "kms-key-1";

        public async Task<byte[]> SignAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            Calls++;
            return _rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    }

    public class JwtSignerTests
    {
        [Fact]
        public async Task GenerateTokenAsync_WithSigner_ShouldProduceVerifiableToken()
        {
            var (privatePem, publicPem) = TestKeys.Rsa();
            var signer = new FakeKmsSigner(new FakeKmsKey(privatePem));

            var options = new JwtOptions
            {
                Issuer = "issuer",
                Audience = "audience",
                PublicKeyPem = publicPem,
                KeyId = "kms-key-1",
                ValidateNotBefore = true,
                Expiration = TimeSpan.FromMinutes(5)
            };

            var service = new JwtTokenService(options, signer);

            var result = await service.GenerateTokenAsync(
                new ClaimsIdentity([new Claim("sub", "42")]),
                [new Claim("tenant", "acme")],
                TestContext.Current.CancellationToken);

            signer.Calls.Should().Be(1);
            result.ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(5));

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
            jwt.Header.Alg.Should().Be("RS256");
            jwt.Header.Kid.Should().Be("kms-key-1");
            jwt.Claims.Should().Contain(c => c.Type == "tenant" && c.Value == "acme");
            jwt.Payload.NotBefore.Should().NotBeNull();

            var validation = await new JsonWebTokenHandler().ValidateTokenAsync(result.Token, new TokenValidationParameters
            {
                ValidIssuer = "issuer",
                ValidAudience = "audience",
                IssuerSigningKeys = JwtKeyMaterial.Create(options).ValidationKeys
            });

            validation.IsValid.Should().BeTrue(validation.Exception?.Message);
        }

        [Fact]
        public void GenerateToken_WithoutSigningKey_ShouldExplain()
        {
            var (_, publicPem) = TestKeys.Rsa();

            var service = new JwtTokenService(new JwtOptions { PublicKeyPem = publicPem });

            var act = () => service.GenerateToken(new ClaimsIdentity());

            act.Should().Throw<InvalidOperationException>().WithMessage("No JWT signing key*");
        }

        private sealed class LegacyTokenService : IJwtTokenService
        {
            public JwtTokenResult GenerateToken(ClaimsIdentity identity, IEnumerable<Claim>? additionalClaims = null) =>
                new() { Token = "legacy", ExpiresAtUtc = DateTime.UtcNow };
        }

        [Fact]
        public async Task ExistingImplementations_ShouldGetGenerateTokenAsyncForFree()
        {
            IJwtTokenService service = new LegacyTokenService();

            var result = await service.GenerateTokenAsync(new ClaimsIdentity(), cancellationToken: TestContext.Current.CancellationToken);

            result.Token.Should().Be("legacy");
        }
    }
}
