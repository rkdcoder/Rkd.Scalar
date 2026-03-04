using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Security.Basic;
using Rkd.Scalar.Security.Contracts;

namespace Rkd.Scalar.Middleware
{

    public class ScalarUiAuthMiddleware
    {
        private readonly RequestDelegate _next;

        public ScalarUiAuthMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!await IsAuthorized(context))
            {
                context.Response.StatusCode = 401;
                context.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Scalar UI\"";
                return;
            }

            await _next(context);
        }

        private async Task<bool> IsAuthorized(HttpContext context)
        {
            var header = context.Request.Headers["Authorization"].FirstOrDefault();

            if (string.IsNullOrEmpty(header))
                return false;

            if (!BasicAuthParser.TryParse(header, out var credentials))
                return false;

            var validator = context.RequestServices.GetService<IUiCredentialValidator>();

            if (validator == null)
                return true;

            var identity = await validator.ValidateAsync(credentials);

            return identity != null;
        }
    }
}