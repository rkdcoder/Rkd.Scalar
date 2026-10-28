using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace Rkd.Scalar
{
    /// <summary>
    /// Outcome of <see cref="ICredentialValidator{TCredentials}.ValidateAsync"/>.
    /// </summary>
    /// <remarks>
    /// A <see cref="ClaimsIdentity"/> converts implicitly to a result (<see langword="null"/> means failure),
    /// so <c>return identity;</c> works inside an async validator.
    /// </remarks>
    public sealed class CredentialValidationResult
    {
        private CredentialValidationResult(ClaimsIdentity? identity, string? failureReason)
        {
            Identity = identity;
            FailureReason = failureReason;
        }

        /// <summary>The authenticated identity when validation succeeded.</summary>
        public ClaimsIdentity? Identity { get; }

        /// <summary>
        /// Why validation failed (for example "Account locked."). Logged by the authentication handlers
        /// and returned as the <c>detail</c> of the login endpoint 401 response, so keep it safe to show.
        /// </summary>
        public string? FailureReason { get; }

        /// <summary>Whether the credentials are valid.</summary>
        [MemberNotNullWhen(true, nameof(Identity))]
        public bool Succeeded => Identity is not null;

        /// <summary>Valid credentials, authenticated as <paramref name="identity"/>.</summary>
        public static CredentialValidationResult Success(ClaimsIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);

            return new CredentialValidationResult(identity, null);
        }

        /// <summary>Valid credentials, authenticated with the given <paramref name="claims"/>.</summary>
        public static CredentialValidationResult Success(params IEnumerable<Claim> claims) =>
            Success(new ClaimsIdentity(claims, "RkdScalar"));

        /// <summary>Invalid credentials.</summary>
        /// <param name="reason">Optional reason, safe to show to the client.</param>
        public static CredentialValidationResult Failure(string? reason = null) =>
            new(null, reason);

        /// <summary>Converts an identity to a result; <see langword="null"/> becomes a failure.</summary>
        public static implicit operator CredentialValidationResult(ClaimsIdentity? identity) =>
            identity is null ? Failure() : Success(identity);
    }
}
