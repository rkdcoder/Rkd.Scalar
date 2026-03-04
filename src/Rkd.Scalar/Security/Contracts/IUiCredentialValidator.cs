using Rkd.Scalar.Security.Basic;

namespace Rkd.Scalar.Security.Contracts
{
    public interface IUiCredentialValidator
        : ICredentialValidator<BasicAuthCredentials>
    {
    }
}
