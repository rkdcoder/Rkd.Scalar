using Rkd.Scalar.Security.Contracts;

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

        public Task<CredentialValidationResult> ValidateAsync(
            BasicAuthCredentials request,
            CancellationToken cancellationToken = default)
        {
            return _inner.ValidateAsync(request, cancellationToken);
        }
    }
}
