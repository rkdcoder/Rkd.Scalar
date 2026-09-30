namespace Rkd.Scalar
{
    /// <summary>
    /// Validates the credentials received by an Rkd.Scalar authentication feature
    /// (JWT login, Basic, API Key or Scalar UI protection).
    /// </summary>
    /// <typeparam name="TCredentials">
    /// The credential model, e.g. <see cref="BasicAuthCredentials"/>, <see cref="ApiKeyCredentials"/>
    /// or your own login model.
    /// </typeparam>
    /// <example>
    /// <code>
    /// public sealed class LoginValidator : ICredentialValidator&lt;LoginRequest&gt;
    /// {
    ///     public async Task&lt;CredentialValidationResult&gt; ValidateAsync(LoginRequest request, CancellationToken ct)
    ///     {
    ///         var user = await users.FindAsync(request.Username, ct);
    ///         if (user is null || !hasher.Verify(request.Password, user.PasswordHash))
    ///             return CredentialValidationResult.Failure("Invalid username or password.");
    ///
    ///         return CredentialValidationResult.Success(
    ///             new Claim(ClaimTypes.NameIdentifier, user.Id),
    ///             new Claim(ClaimTypes.Name, user.Username));
    ///     }
    /// }
    /// </code>
    /// </example>
    public interface ICredentialValidator<in TCredentials>
    {
        /// <summary>
        /// Validates <paramref name="credentials"/>.
        /// </summary>
        /// <param name="credentials">The credentials sent by the client.</param>
        /// <param name="cancellationToken">Cancelled when the request is aborted.</param>
        /// <returns>
        /// <see cref="CredentialValidationResult.Success(System.Security.Claims.ClaimsIdentity)"/> with the
        /// authenticated identity, or <see cref="CredentialValidationResult.Failure(string?)"/>.
        /// </returns>
        Task<CredentialValidationResult> ValidateAsync(
            TCredentials credentials,
            CancellationToken cancellationToken = default);
    }
}
