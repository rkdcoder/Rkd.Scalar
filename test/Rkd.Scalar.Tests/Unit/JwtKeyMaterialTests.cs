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
    public class JwtKeyMaterialTests
    {
        private static JwtOptions Options(Action<JwtOptions> configure)
        {
            var options = new JwtOptions { Issuer = "issuer", Audience = "audience" };
            configure(options);
            return options;
        }

        private static async Task<TokenValidationResult> ValidateAsync(string token, JwtOptions options)
        {
            var keys = JwtKeyMaterial.Create(options);

            return await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
            {
                ValidIssuer = options.Issuer,
                ValidAudience = options.Audience,
                IssuerSigningKeys = keys.ValidationKeys
            });
        }

        [Fact]
        public void Secret_ShouldProduceHs256()
        {
            var keys = JwtKeyMaterial.Create(Options(o => o.Secret = new string('a', 32)));

            keys.SigningKey.Should().BeOfType<SymmetricSecurityKey>();
            keys.SigningAlgorithm.Should().Be(SecurityAlgorithms.HmacSha256);
            keys.SigningKey!.KeyId.Should().BeNull("HMAC tokens keep the 1.0 header (no kid)");
        }

        [Fact]
        public void ShortSecret_ShouldThrow()
        {
            var act = () => JwtKeyMaterial.Create(Options(o => o.Secret = "short"));

            act.Should().Throw<InvalidOperationException>().WithMessage("*at least 32 characters*");
        }

        [Fact]
        public void NoKey_ShouldThrow()
        {
            var act = () => JwtKeyMaterial.Create(Options(_ => { }));

            act.Should().Throw<InvalidOperationException>().WithMessage("No JWT key configured*");
        }

        [Fact]
        public void SecretAndPrivateKey_ShouldThrow()
        {
            var (privatePem, _) = TestKeys.Rsa();

            var act = () => JwtKeyMaterial.Create(Options(o =>
            {
                o.Secret = new string('a', 32);
                o.PrivateKeyPem = privatePem;
            }));

            act.Should().Throw<InvalidOperationException>().WithMessage("*not both*");
        }

        [Fact]
        public void InvalidPem_ShouldThrow()
        {
            var act = () => JwtKeyMaterial.Create(Options(o => o.PublicKeyPem = "not a key"));

            act.Should().Throw<InvalidOperationException>().WithMessage("*valid PEM*");
        }

        [Fact]
        public void MissingKeyFile_ShouldThrow()
        {
            var act = () => JwtKeyMaterial.Create(Options(o => o.PrivateKeyPath = "/does/not/exist.pem"));

            act.Should().Throw<InvalidOperationException>().WithMessage("*was not found*");
        }

        [Fact]
        public async Task RsaPrivateKey_ShouldSignRs256_AndValidateWithPublicKey()
        {
            var (privatePem, publicPem) = TestKeys.Rsa();

            var signing = Options(o => o.PrivateKeyPem = privatePem);
            var token = new JwtTokenService(signing).GenerateToken(new ClaimsIdentity([new Claim("sub", "42")]));

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Token);
            jwt.Header.Alg.Should().Be(SecurityAlgorithms.RsaSha256);
            jwt.Header.Kid.Should().NotBeNullOrEmpty();

            var result = await ValidateAsync(token.Token, Options(o => o.PublicKeyPem = publicPem));
            result.IsValid.Should().BeTrue(result.Exception?.Message);
        }

        [Fact]
        public async Task EcPrivateKeyFromFile_ShouldSignEs256()
        {
            var (privatePem, publicPem) = TestKeys.EcP256();
            var path = Path.Combine(Path.GetTempPath(), $"rkd-{Guid.NewGuid():N}.pem");
            await File.WriteAllTextAsync(path, privatePem, TestContext.Current.CancellationToken);

            try
            {
                var token = new JwtTokenService(Options(o => o.PrivateKeyPath = path))
                    .GenerateToken(new ClaimsIdentity([new Claim("sub", "42")]));

                new JwtSecurityTokenHandler().ReadJwtToken(token.Token).Header.Alg
                    .Should().Be(SecurityAlgorithms.EcdsaSha256);

                var result = await ValidateAsync(token.Token, Options(o => o.PublicKeyPem = publicPem));
                result.IsValid.Should().BeTrue(result.Exception?.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void PemWithEscapedNewLines_ShouldBeAccepted()
        {
            var (_, publicPem) = TestKeys.Rsa();
            var escaped = publicPem.Replace("\n", "\\n");

            var keys = JwtKeyMaterial.Create(Options(o => o.PublicKeyPem = escaped));

            keys.ValidationKeys.Should().ContainSingle().Which.Should().BeOfType<RsaSecurityKey>();
            keys.SigningKey.Should().BeNull();
        }

        [Fact]
        public void ExplicitAlgorithmAndKeyId_ShouldBeUsed()
        {
            var (privatePem, _) = TestKeys.Rsa();

            var token = new JwtTokenService(Options(o =>
            {
                o.PrivateKeyPem = privatePem;
                o.Algorithm = SecurityAlgorithms.RsaSsaPssSha256;
                o.KeyId = "key-2026";
            })).GenerateToken(new ClaimsIdentity());

            var header = new JwtSecurityTokenHandler().ReadJwtToken(token.Token).Header;
            header.Alg.Should().Be(SecurityAlgorithms.RsaSsaPssSha256);
            header.Kid.Should().Be("key-2026");
        }

        [Fact]
        public void PublicJsonWebKeys_ShouldNeverExposePrivateParameters()
        {
            var (privatePem, _) = TestKeys.Rsa();

            var jwks = JwtKeyMaterial.Create(Options(o => o.PrivateKeyPem = privatePem)).GetPublicJsonWebKeys();

            var jwk = jwks.Should().ContainSingle().Subject;
            jwk.Kty.Should().Be("RSA");
            jwk.Alg.Should().Be(SecurityAlgorithms.RsaSha256);
            jwk.Use.Should().Be("sig");
            jwk.HasPrivateKey.Should().BeFalse();
            jwk.D.Should().BeNull();
        }

        [Fact]
        public void PublicJsonWebKeys_ShouldSkipSymmetricKeys()
        {
            JwtKeyMaterial.Create(Options(o => o.Secret = new string('a', 32)))
                .GetPublicJsonWebKeys()
                .Should().BeEmpty();
        }

        [Fact]
        public async Task ValidationKeys_ShouldAcceptPreviousKeyDuringRotation()
        {
            var (oldPrivate, _) = TestKeys.Rsa();
            var (newPrivate, _) = TestKeys.Rsa();

            var oldToken = new JwtTokenService(Options(o => o.PrivateKeyPem = oldPrivate))
                .GenerateToken(new ClaimsIdentity());

            using var oldRsa = RSA.Create();
            oldRsa.ImportFromPem(oldPrivate);
            var oldKeyPublic = new RsaSecurityKey(oldRsa.ExportParameters(false));
            oldKeyPublic.KeyId = new JwtSecurityTokenHandler().ReadJwtToken(oldToken.Token).Header.Kid;

            var result = await ValidateAsync(oldToken.Token, Options(o =>
            {
                o.PrivateKeyPem = newPrivate;
                o.ValidationKeys.Add(oldKeyPublic);
            }));

            result.IsValid.Should().BeTrue(result.Exception?.Message);
        }
    }
}
