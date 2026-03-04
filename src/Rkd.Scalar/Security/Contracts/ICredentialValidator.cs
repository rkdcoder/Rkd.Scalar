using System.Security.Claims;

namespace Rkd.Scalar.Security.Contracts
{
    public interface ICredentialValidator<in TRequest>
    {
        Task<ClaimsIdentity?> ValidateAsync(
            TRequest request,
            CancellationToken cancellationToken = default);
    }
}
