using System.Security.Claims;

namespace Rkd.Scalar.Security.Jwt
{
    /// <summary>
    /// Issues JWT access tokens for an authenticated identity.
    /// </summary>
    public interface IJwtTokenService
    {
        /// <summary>
        /// Generates a signed JWT containing the claims of <paramref name="identity"/>.
        /// </summary>
        /// <param name="identity">Authenticated identity whose claims are written to the token.</param>
        /// <param name="additionalClaims">Extra claims appended to the token.</param>
        JwtTokenResult GenerateToken(
            ClaimsIdentity identity,
            IEnumerable<Claim>? additionalClaims = null);

        /// <summary>
        /// Generates a signed JWT asynchronously. Prefer this overload: it supports remote/async signing
        /// through <see cref="IJwtSigner"/> and cancellation.
        /// </summary>
        /// <param name="identity">Authenticated identity whose claims are written to the token.</param>
        /// <param name="additionalClaims">Extra claims appended to the token.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        Task<JwtTokenResult> GenerateTokenAsync(
            ClaimsIdentity identity,
            IEnumerable<Claim>? additionalClaims = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(GenerateToken(identity, additionalClaims));
    }
}
