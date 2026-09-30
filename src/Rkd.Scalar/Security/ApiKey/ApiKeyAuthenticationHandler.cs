using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rkd.Scalar.Security.Contracts;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Rkd.Scalar.Security.ApiKey
{
    internal sealed class ApiKeyAuthenticationHandler<TValidator>
        : AuthenticationHandler<ApiKeyAuthenticationOptions>
        where TValidator : class, ICredentialValidator<ApiKeyCredentials>
    {
        private readonly TValidator _validator;

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<ApiKeyAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            TValidator validator)
            : base(options, logger, encoder)
        {
            _validator = validator;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(Options.HeaderName, out var key) ||
                string.IsNullOrWhiteSpace(key))
                return AuthenticateResult.NoResult();

            var credentials = new ApiKeyCredentials(key!);

            var identity = await _validator.ValidateAsync(credentials, Context.RequestAborted);

            if (identity == null)
                return AuthenticateResult.Fail("Invalid API Key");

            var principal = new ClaimsPrincipal(identity);

            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
    }
}
