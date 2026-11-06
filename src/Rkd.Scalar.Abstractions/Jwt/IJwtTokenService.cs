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

        /// <summary>
        /// Creates a signed JWT that expires at <paramref name="expiresAt"/> instead of the configured expiration, for
        /// tokens whose validity depends on the case (a temporary access, a service token, a short-lived link).
        /// </summary>
        /// <param name="identity">Authenticated identity whose claims are written to the token.</param>
        /// <param name="expiresAt">
        /// When the token expires. <c>exp</c> has whole seconds, so fractions are dropped: the token never outlives this instant.
        /// </param>
        /// <param name="additionalClaims">Extra claims appended to the token.</param>
        /// <param name="cancellationToken">Token used to cancel the operation (relevant with an <see cref="IJwtSigner"/>).</param>
        /// <returns>The token; <see cref="JwtToken.ExpiresAt"/> has the value written to <c>exp</c>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="expiresAt"/> is not after the moment of issue.</exception>
        /// <exception cref="NotSupportedException">The implementation does not override this method.</exception>
        /// <remarks>
        /// Validators still apply their clock skew (<c>JwtOptions.ClockSkewInSeconds</c>) to <c>exp</c>. A custom implementation
        /// compiled before 2.11 does not support this: it fails here instead of issuing a token with another validity.
        /// </remarks>
        Task<JwtToken> CreateTokenAsync(
            ClaimsIdentity identity,
            DateTimeOffset expiresAt,
            IEnumerable<Claim>? additionalClaims = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                $"{GetType().FullName} does not support a validity per token: override " +
                $"{nameof(IJwtTokenService)}.{nameof(CreateTokenAsync)}(ClaimsIdentity, DateTimeOffset, ...).");

        /// <summary>
        /// Creates a signed JWT valid for <paramref name="lifetime"/> from now, instead of the configured expiration.
        /// </summary>
        /// <param name="identity">Authenticated identity whose claims are written to the token.</param>
        /// <param name="lifetime">How long the token is valid, down to whole seconds. Must be at least one second.</param>
        /// <param name="additionalClaims">Extra claims appended to the token.</param>
        /// <param name="cancellationToken">Token used to cancel the operation (relevant with an <see cref="IJwtSigner"/>).</param>
        /// <returns>The token; <see cref="JwtToken.ExpiresAt"/> has the value written to <c>exp</c>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetime"/> is shorter than one second.</exception>
        /// <exception cref="NotSupportedException">
        /// The implementation does not override this method nor the one that receives the expiration instant.
        /// </exception>
        /// <remarks>An implementation that overrides the method with the expiration instant gets this one for free.</remarks>
        Task<JwtToken> CreateTokenAsync(
            ClaimsIdentity identity,
            TimeSpan lifetime,
            IEnumerable<Claim>? additionalClaims = null,
            CancellationToken cancellationToken = default)
        {
            if (lifetime <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "The lifetime of a token must be positive.");

            return CreateTokenAsync(identity, DateTimeOffset.UtcNow.Add(lifetime), additionalClaims, cancellationToken);
        }
    }
}
