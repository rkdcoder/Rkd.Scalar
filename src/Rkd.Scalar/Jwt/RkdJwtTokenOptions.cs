using System.Security.Claims;

namespace Rkd.Scalar
{
    /// <summary>
    /// Options of the service token added to outgoing requests by <c>AddRkdJwtToken</c>.
    /// </summary>
    public sealed class RkdJwtTokenOptions
    {
        /// <summary>
        /// How long before expiration a new token is created. Defaults to 1 minute (never more than half the token lifetime).
        /// </summary>
        public TimeSpan RefreshBeforeExpiration { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Calls made while handling a request forward that request's <c>Authorization: Bearer</c> header (the user's
        /// own token), so the other API sees the logged user. A service token is issued only when there is no current
        /// request or it has no Bearer token (background jobs, anonymous or Basic/API Key requests).
        /// Defaults to <see langword="false"/>.
        /// </summary>
        /// <remarks>The other API must accept those tokens (same issuer, and its audience — see <c>JwtOptions.AdditionalAudiences</c>).</remarks>
        public bool ForwardIncomingToken { get; set; }

        /// <summary>Extra claims written to the token (e.g. <c>scope</c>).</summary>
        public IList<Claim> AdditionalClaims { get; } = new List<Claim>();
    }
}
