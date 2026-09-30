using Microsoft.AspNetCore.Authentication;

namespace Rkd.Scalar
{
    /// <summary>
    /// Options of the API Key authentication scheme.
    /// </summary>
    public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
    {
        /// <summary>
        /// Default request header carrying the API key.
        /// </summary>
        public const string DefaultHeaderName = "X-API-Key";

        /// <summary>
        /// Request header carrying the API key. Defaults to <c>X-API-Key</c>.
        /// </summary>
        public string HeaderName { get; set; } = DefaultHeaderName;
    }
}
