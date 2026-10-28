using System.Security.Cryptography;
using Rkd.Scalar.Security.Jwt;

namespace Rkd.Scalar
{
    /// <summary>
    /// Imports PEM private keys the way Rkd.Scalar does for <c>JwtOptions.PrivateKeyPem</c>: decoded in managed code and
    /// imported with <c>ImportParameters</c>, so they also load on IIS application pools without "Load User Profile"
    /// (where <c>ImportFromPem</c> fails with <c>The system cannot find the file specified</c>). Use it for keys you
    /// manage yourself (e.g. stored in a database) outside <see cref="IJwtTokenService"/>.
    /// </summary>
    /// <remarks>
    /// Managed decoding: PKCS#8 <c>PRIVATE KEY</c> (RSA and EC), PKCS#1 <c>RSA PRIVATE KEY</c> and SEC1 <c>EC PRIVATE KEY</c>
    /// on P-256, P-384 and P-521. Other formats (encrypted keys, other curves) fall back to <c>ImportFromPem</c>.
    /// The caller owns (and disposes) the returned key.
    /// </remarks>
    /// <example>
    /// <code>
    /// using var ecdsa = RkdPem.ImportECDsaPrivateKey(keyFromDatabase);
    /// var credentials = new SigningCredentials(new ECDsaSecurityKey(ecdsa) { KeyId = kid }, SecurityAlgorithms.EcdsaSha256);
    /// </code>
    /// </example>
    public static class RkdPem
    {
        /// <summary>Imports an RSA or ECDSA private key.</summary>
        /// <param name="pem">PEM text of the private key.</param>
        /// <returns>An <see cref="RSA"/> or <see cref="ECDsa"/> instance.</returns>
        /// <exception cref="CryptographicException">The PEM does not contain a supported private key.</exception>
        public static AsymmetricAlgorithm ImportPrivateKey(string pem)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pem);

            if (PemPrivateKeyReader.TryImport(pem) is { } key)
                return key;

            var rsa = RSA.Create();

            try
            {
                rsa.ImportFromPem(pem);
                return rsa;
            }
            catch (Exception ex) when (ex is ArgumentException or CryptographicException)
            {
                rsa.Dispose();
            }

            var ecdsa = ECDsa.Create();

            try
            {
                ecdsa.ImportFromPem(pem);
                return ecdsa;
            }
            catch (Exception ex) when (ex is ArgumentException or CryptographicException)
            {
                ecdsa.Dispose();
                throw new CryptographicException("The PEM does not contain a supported RSA or ECDSA private key.", ex);
            }
        }

        /// <summary>Imports an ECDSA private key.</summary>
        /// <param name="pem">PEM text of the private key (<c>PRIVATE KEY</c> or <c>EC PRIVATE KEY</c>).</param>
        /// <returns>The key.</returns>
        /// <exception cref="CryptographicException">The PEM does not contain an ECDSA private key.</exception>
        public static ECDsa ImportECDsaPrivateKey(string pem) => ImportPrivateKey(pem) switch
        {
            ECDsa ecdsa => ecdsa,
            var other => throw Mismatch(other, "ECDSA")
        };

        /// <summary>Imports an RSA private key.</summary>
        /// <param name="pem">PEM text of the private key (<c>PRIVATE KEY</c> or <c>RSA PRIVATE KEY</c>).</param>
        /// <returns>The key.</returns>
        /// <exception cref="CryptographicException">The PEM does not contain an RSA private key.</exception>
        public static RSA ImportRsaPrivateKey(string pem) => ImportPrivateKey(pem) switch
        {
            RSA rsa => rsa,
            var other => throw Mismatch(other, "RSA")
        };

        private static CryptographicException Mismatch(AsymmetricAlgorithm key, string expected)
        {
            var actual = key is RSA ? "RSA" : "ECDSA";
            key.Dispose();

            return new CryptographicException($"The PEM contains an {actual} key, not an {expected} key.");
        }
    }
}
