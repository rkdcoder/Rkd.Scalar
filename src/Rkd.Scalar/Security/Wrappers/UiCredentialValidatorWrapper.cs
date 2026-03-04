using Rkd.Scalar.Security.Basic;
using Rkd.Scalar.Security.Contracts;
using System.Security.Claims;

namespace Rkd.Scalar.Security.Wrappers
{
    internal sealed class UiCredentialValidatorWrapper<T>
        : IUiCredentialValidator
        where T : ICredentialValidator<BasicAuthCredentials>
    {
        private readonly T _inner;

        public UiCredentialValidatorWrapper(T inner)
        {
            _inner = inner;
        }

        public Task<ClaimsIdentity?> ValidateAsync(
            BasicAuthCredentials request,
            CancellationToken cancellationToken = default)
        {
            return _inner.ValidateAsync(request, cancellationToken);
        }
    }
}
