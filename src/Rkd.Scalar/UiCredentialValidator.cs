using Rkd.Scalar.Security.Basic;
using Rkd.Scalar.Security.Contracts;
using System.Security.Claims;

namespace testeee
{
    public class UiCredentialValidator : ICredentialValidator<BasicAuthCredentials>
    {
        public Task<ClaimsIdentity?> ValidateAsync(
            BasicAuthCredentials request,
            CancellationToken cancellationToken = default)
        {
            if (request.Username == "admin" && request.Password == "123")
            {
                var identity = new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Name, request.Username), new Claim(ClaimTypes.Role, "ADMIN") },
                    "Basic"
                );

                return Task.FromResult<ClaimsIdentity?>(identity);
            }

            return Task.FromResult<ClaimsIdentity?>(null);
        }
    }
}
