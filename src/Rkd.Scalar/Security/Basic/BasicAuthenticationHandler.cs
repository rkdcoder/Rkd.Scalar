using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Rkd.Scalar.Security.Basic
{
    internal sealed class BasicAuthenticationHandler<TValidator>
        : AuthenticationHandler<AuthenticationSchemeOptions>
        where TValidator : class, ICredentialValidator<BasicAuthCredentials>
    {
        private readonly TValidator _validator;

        public BasicAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            TValidator validator)
            : base(options, logger, encoder)
        {
            _validator = validator;
        }

        protected override async Task<AuthenticateResult>
            HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(
                "Authorization",
                out var header))
            {
                return AuthenticateResult.NoResult();
            }

            if (!BasicAuthParser.TryParse(
                header!,
                out var credentials))
            {
                return AuthenticateResult.Fail(
                    "Invalid Basic Authorization header");
            }

            var result = await _validator.ValidateAsync(credentials, Context.RequestAborted);

            if (result is not { Succeeded: true })
            {
                return AuthenticateResult.Fail(
                    result?.FailureReason ?? "Invalid username or password");
            }

            var principal = new ClaimsPrincipal(result.Identity);

            var ticket = new AuthenticationTicket(
                principal,
                Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
    }
}
