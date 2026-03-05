using System.Security.Claims;

namespace Rkd.Scalar.Security.Contracts
{
    /// <summary>
    /// Defines the contract responsible for validating authentication credentials.
    /// </summary>
    /// <typeparam name="TRequest">
    /// The credential model received during the authentication process.
    /// Examples include <c>BasicAuthCredentials</c>, <c>ApiKeyCredentials</c>
    /// or any custom credential type used for JWT login.
    /// </typeparam>
    /// <remarks>
    /// Implementations of this interface are responsible for validating incoming
    /// authentication data and producing a <see cref="ClaimsIdentity"/> when the
    /// credentials are valid.
    ///
    /// If validation fails, the method should return <see langword="null"/>.
    ///
    /// The resulting <see cref="ClaimsIdentity"/> is used to create the authenticated
    /// <see cref="ClaimsPrincipal"/> associated with the request or to generate
    /// a JWT token when using Bearer authentication.
    /// </remarks>
    public interface ICredentialValidator<in TRequest>
    {
        /// <summary>
        /// Validates the provided credentials and returns a <see cref="ClaimsIdentity"/>
        /// when authentication succeeds.
        /// </summary>
        /// <param name="request">
        /// The credential payload sent by the client.
        /// </param>
        /// <param name="cancellationToken">
        /// Token used to cancel the validation operation.
        /// </param>
        /// <returns>
        /// A valid <see cref="ClaimsIdentity"/> if authentication succeeds;
        /// otherwise <see langword="null"/>.
        /// </returns>
        Task<ClaimsIdentity?> ValidateAsync(
            TRequest request,
            CancellationToken cancellationToken = default);
    }
}
