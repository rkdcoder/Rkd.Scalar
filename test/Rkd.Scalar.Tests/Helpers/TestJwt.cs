using Rkd.Scalar.Security.Jwt;

namespace Rkd.Scalar.Tests.Helpers
{
    public static class TestJwt
    {
        public static JwtOptions Options(Action<JwtOptions>? configure = null)
        {
            var options = new JwtOptions { Issuer = "issuer", Audience = "audience" };
            configure?.Invoke(options);
            return options;
        }

        internal static JwtTokenService Service(JwtOptions options, IJwtSigner? signer = null) =>
            new(options, JwtKeyMaterial.Create(options), signer);
    }
}
