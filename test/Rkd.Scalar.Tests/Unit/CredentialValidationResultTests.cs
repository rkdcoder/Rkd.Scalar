using FluentAssertions;
using System.Security.Claims;

namespace Rkd.Scalar.Tests.Unit
{
    public class CredentialValidationResultTests
    {
        [Fact]
        public void Success_ShouldCarryIdentity()
        {
            var result = CredentialValidationResult.Success(new Claim(ClaimTypes.Name, "ana"));

            result.Succeeded.Should().BeTrue();
            result.Identity!.Name.Should().Be("ana");
            result.Identity.IsAuthenticated.Should().BeTrue();
            result.FailureReason.Should().BeNull();
        }

        [Fact]
        public void Failure_ShouldCarryReason()
        {
            var result = CredentialValidationResult.Failure("Account locked.");

            result.Succeeded.Should().BeFalse();
            result.Identity.Should().BeNull();
            result.FailureReason.Should().Be("Account locked.");
        }

        [Fact]
        public void ImplicitConversion_ShouldMapIdentityAndNull()
        {
            CredentialValidationResult success = new ClaimsIdentity([new Claim(ClaimTypes.Name, "ana")], "Test");
            CredentialValidationResult failure = (ClaimsIdentity?)null;

            success.Succeeded.Should().BeTrue();
            failure.Succeeded.Should().BeFalse();
        }
    }
}
