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

        /// <summary>Extra claims written to the token (e.g. <c>scope</c>).</summary>
        public IList<Claim> AdditionalClaims { get; } = new List<Claim>();
    }
}
