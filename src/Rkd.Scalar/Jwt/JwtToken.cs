namespace Rkd.Scalar
{
    /// <summary>
    /// A signed JWT created by <see cref="IJwtTokenService"/>.
    /// </summary>
    /// <param name="AccessToken">The serialized (compact) JWT.</param>
    /// <param name="TokenId">The <c>jti</c> claim, useful to revoke or audit the token.</param>
    /// <param name="IssuedAt">When the token was issued (<c>iat</c>).</param>
    /// <param name="ExpiresAt">When the token expires (<c>exp</c>).</param>
    public sealed record JwtToken(
        string AccessToken,
        string TokenId,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt);
}
