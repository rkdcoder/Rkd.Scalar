using System.Security.Cryptography;

namespace Rkd.Scalar.Tests.Helpers
{
    public static class TestKeys
    {
        public static (string PrivatePem, string PublicPem) Rsa()
        {
            using var rsa = RSA.Create(2048);
            return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
        }

        public static (string PrivatePem, string PublicPem) EcP256()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return (ecdsa.ExportECPrivateKeyPem(), ecdsa.ExportSubjectPublicKeyInfoPem());
        }
    }
}
