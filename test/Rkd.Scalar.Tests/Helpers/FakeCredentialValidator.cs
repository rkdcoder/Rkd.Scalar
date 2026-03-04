using Rkd.Scalar.Security.Basic;
using Rkd.Scalar.Security.Contracts;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;

namespace Rkd.Scalar.Tests.Helpers
{
    public class FakeCredentialValidator : ICredentialValidator<BasicAuthCredentials>, ICredentialValidator<TestCredentials>
    {
        public Task<ClaimsIdentity?> ValidateAsync(BasicAuthCredentials request, CancellationToken cancellationToken = default)
        {
            if (request.Username == "admin" && request.Password == "correct")
            {
                return Task.FromResult<ClaimsIdentity?>(new ClaimsIdentity([
                    new Claim(ClaimTypes.Name, "admin"),
                new Claim("role", "admin")
                ], "Basic"));
            }
            return Task.FromResult<ClaimsIdentity?>(null);
        }

        public Task<ClaimsIdentity?> ValidateAsync(TestCredentials request, CancellationToken cancellationToken = default)
        {
            if (request.Username == "user" && request.Password == "pass")
            {
                return Task.FromResult<ClaimsIdentity?>(new ClaimsIdentity([
                    new Claim(ClaimTypes.Name, "user")
                ], "Bearer"));
            }
            return Task.FromResult<ClaimsIdentity?>(null);
        }
    }
}
