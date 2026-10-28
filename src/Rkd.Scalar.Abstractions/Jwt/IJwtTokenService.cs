using System.Security.Claims;

namespace Rkd.Scalar
{
    /// <summary>
    /// Issues JWT access tokens. Registered by <c>WithBearerAuth&lt;TCredentials, TValidator&gt;()</c>;
    /// inject it in your own login endpoint.
    /// </summary>
    public interface IJwtTokenService
    {
        /// <summary>
        /// Creates a signed JWT with the claims of <paramref name="identity"/>, plus <c>iss</c>, <c>aud</c>,
        /// <c>iat</c>, <c>nbf</c>, <c>exp</c> and <c>jti</c>.
        /// </summary>
        /// <param name="identity">Authenticated identity whose claims are written to the token.</param>
        /// <param name="additionalClaims">Extra claims appended to the token.</param>
        /// <param name="cancellationToken">Token used to cancel the operation (relevant with an <see cref="IJwtSigner"/>).</param>
        Task<JwtToken> CreateTokenAsync(
            ClaimsIdentity identity,
            IEnumerable<Claim>? additionalClaims = null,
            CancellationToken cancellationToken = default);
    }
}
