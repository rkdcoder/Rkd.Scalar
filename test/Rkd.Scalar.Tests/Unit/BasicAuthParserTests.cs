using Rkd.Scalar.Security.Basic;
using FluentAssertions;

namespace Rkd.Scalar.Tests.Unit
{
    public class BasicAuthParserTests
    {
        [Fact]
        public void TryParse_ShouldReturnFalse_WhenHeaderIsEmpty()
        {
            var result = BasicAuthParser.TryParse("", out var creds);
            result.Should().BeFalse();
            creds.Should().BeNull();
        }

        [Fact]
        public void TryParse_ShouldReturnFalse_WhenSchemeIsNotBasic()
        {
            var result = BasicAuthParser.TryParse("Bearer token123", out _);
            result.Should().BeFalse();
        }

        [Fact]
        public void TryParse_ShouldReturnTrue_AndCredentials_WhenHeaderIsValid()
        {
            // "admin:123456" em base64 -> YWRtaW46MTIzNDU2
            var header = "Basic YWRtaW46MTIzNDU2";

            var result = BasicAuthParser.TryParse(header, out var creds);

            result.Should().BeTrue();
            creds.Username.Should().Be("admin");
            creds.Password.Should().Be("123456");
        }

        [Fact]
        public void TryParse_ShouldHandleColonInPassword()
        {
            // "user:pass:word" -> base64
            var raw = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("user:pass:word"));
            var header = $"Basic {raw}";

            var result = BasicAuthParser.TryParse(header, out var creds);

            result.Should().BeTrue();
            creds.Username.Should().Be("user");
            creds.Password.Should().Be("pass:word");
        }
    }
}
