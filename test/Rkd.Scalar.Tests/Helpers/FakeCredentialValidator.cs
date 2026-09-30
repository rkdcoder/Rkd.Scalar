using System.Security.Claims;

namespace Rkd.Scalar.Tests.Helpers
{
    public class FakeCredentialValidator : ICredentialValidator<BasicAuthCredentials>, ICredentialValidator<TestCredentials>
    {
        public Task<CredentialValidationResult> ValidateAsync(BasicAuthCredentials request, CancellationToken cancellationToken = default)
        {
            if (request.Username == "admin" && request.Password == "correct")
            {
                return Task.FromResult(CredentialValidationResult.Success(
                    new Claim(ClaimTypes.Name, "admin"),
                    new Claim(ClaimTypes.Role, "admin")));
            }

            return Task.FromResult(CredentialValidationResult.Failure());
        }

        public async Task<CredentialValidationResult> ValidateAsync(TestCredentials request, CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            if (request.Username == "locked")
                return CredentialValidationResult.Failure("Account locked.");

            if (request.Username != "user" || request.Password != "pass")
                return null as ClaimsIdentity;   // implicit conversion: null identity == failure

            // implicit conversion from ClaimsIdentity
            return new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "42"),
                new Claim(ClaimTypes.Name, "user"),
                new Claim(ClaimTypes.Role, "reader"),
                new Claim(ClaimTypes.Role, "writer"),
                new Claim("tenant", "acme")
            ], "Test");
        }
    }
}
