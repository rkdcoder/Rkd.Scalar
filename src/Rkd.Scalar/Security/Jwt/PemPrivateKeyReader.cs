using System.Formats.Asn1;
using System.Security.Cryptography;

namespace Rkd.Scalar.Security.Jwt
{
    /// <summary>
    /// Reads PEM private keys in managed code (<see cref="PemEncoding"/> + <see cref="AsnReader"/>) and imports them
    /// with <c>ImportParameters</c>. <c>ImportFromPem</c> goes through the CNG PKCS#8 import on Windows, which fails
    /// under IIS application pools without "Load User Profile" (<c>The system cannot find the file specified</c>);
    /// importing the parameters of an ephemeral key does not need the profile.
    /// </summary>
    /// <remarks>
    /// Supported: PKCS#8 <c>PRIVATE KEY</c> (RSA and EC), PKCS#1 <c>RSA PRIVATE KEY</c> and SEC1 <c>EC PRIVATE KEY</c>
    /// on P-256, P-384 and P-521. Anything else (encrypted keys, other curves) returns <see langword="null"/> so the
    /// caller falls back to <c>ImportFromPem</c>.
    /// </remarks>
    internal static class PemPrivateKeyReader
    {
        private const string RsaEncryption = "1.2.840.113549.1.1.1";

        private const string EcPublicKey = "1.2.840.10045.2.1";

        private static readonly Dictionary<string, (ECCurve Curve, int Size)> Curves = new(StringComparer.Ordinal)
        {
            ["1.2.840.10045.3.1.7"] = (ECCurve.NamedCurves.nistP256, 32),
            ["1.3.132.0.34"] = (ECCurve.NamedCurves.nistP384, 48),
            ["1.3.132.0.35"] = (ECCurve.NamedCurves.nistP521, 66)
        };

        /// <summary>Imports the first private key of <paramref name="pem"/>, or returns <see langword="null"/> when its format is not handled here.</summary>
        public static AsymmetricAlgorithm? TryImport(string pem)
        {
            if (!PemEncoding.TryFind(pem, out var fields))
                return null;

            var label = pem[fields.Label];
            byte[] der;

            try
            {
                der = Convert.FromBase64String(pem[fields.Base64Data]);
            }
            catch (FormatException)
            {
                return null;
            }

            try
            {
                return label switch
                {
                    "PRIVATE KEY" => ReadPkcs8(der),
                    "RSA PRIVATE KEY" => CreateRsa(ReadPkcs1(der)),
                    "EC PRIVATE KEY" => CreateEcdsa(ReadSec1(der, curveOid: null)),
                    _ => null
                };
            }
            catch (Exception ex) when (ex is AsnContentException or CryptographicException or KeyNotFoundException or ArgumentException)
            {
                return null;
            }
        }

        private static AsymmetricAlgorithm? ReadPkcs8(byte[] der)
        {
            var reader = new AsnReader(der, AsnEncodingRules.DER);
            var info = reader.ReadSequence();

            info.ReadInteger(); // version

            var algorithm = info.ReadSequence();
            var oid = algorithm.ReadObjectIdentifier();
            string? curveOid = null;

            if (algorithm.HasData)
            {
                if (algorithm.PeekTag().HasSameClassAndValue(Asn1Tag.ObjectIdentifier))
                    curveOid = algorithm.ReadObjectIdentifier();
                else
                    algorithm.ReadNull();
            }

            var privateKey = info.ReadOctetString();

            return oid switch
            {
                RsaEncryption => CreateRsa(ReadPkcs1(privateKey)),
                EcPublicKey => CreateEcdsa(ReadSec1(privateKey, curveOid)),
                _ => null
            };
        }

        private static RSAParameters ReadPkcs1(byte[] der)
        {
            var reader = new AsnReader(der, AsnEncodingRules.DER);
            var key = reader.ReadSequence();

            key.ReadInteger(); // version

            var modulus = Unsigned(key.ReadIntegerBytes().Span);
            var exponent = Unsigned(key.ReadIntegerBytes().Span);
            var d = key.ReadIntegerBytes();
            var p = key.ReadIntegerBytes();
            var q = key.ReadIntegerBytes();
            var dp = key.ReadIntegerBytes();
            var dq = key.ReadIntegerBytes();
            var inverseQ = key.ReadIntegerBytes();

            // RSAParameters requires D to be as long as the modulus and the CRT values half of it.
            var half = (modulus.Length + 1) / 2;

            return new RSAParameters
            {
                Modulus = modulus,
                Exponent = exponent,
                D = Fixed(d.Span, modulus.Length),
                P = Fixed(p.Span, half),
                Q = Fixed(q.Span, half),
                DP = Fixed(dp.Span, half),
                DQ = Fixed(dq.Span, half),
                InverseQ = Fixed(inverseQ.Span, half)
            };
        }

        private static ECParameters ReadSec1(byte[] der, string? curveOid)
        {
            var reader = new AsnReader(der, AsnEncodingRules.DER);
            var key = reader.ReadSequence();

            key.ReadInteger(); // version (1)

            var privateKey = key.ReadOctetString();
            byte[]? publicKey = null;

            var parametersTag = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
            var publicKeyTag = new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true);

            if (key.HasData && key.PeekTag().HasSameClassAndValue(parametersTag))
            {
                var parameters = key.ReadSequence(parametersTag);
                curveOid ??= parameters.ReadObjectIdentifier();
            }

            if (key.HasData && key.PeekTag().HasSameClassAndValue(publicKeyTag))
            {
                var wrapper = key.ReadSequence(publicKeyTag);
                publicKey = wrapper.ReadBitString(out _);
            }

            if (curveOid is null || !Curves.TryGetValue(curveOid, out var curve))
                throw new CryptographicException("Unsupported EC curve.");

            var parametersResult = new ECParameters
            {
                Curve = curve.Curve,
                D = Fixed(privateKey, curve.Size)
            };

            // Uncompressed point: 0x04 || X || Y. Without it, the platform derives Q from D.
            if (publicKey is { Length: > 0 } && publicKey[0] == 0x04 && publicKey.Length == 1 + 2 * curve.Size)
            {
                parametersResult.Q = new ECPoint
                {
                    X = publicKey.AsSpan(1, curve.Size).ToArray(),
                    Y = publicKey.AsSpan(1 + curve.Size, curve.Size).ToArray()
                };
            }
            else if (publicKey is { Length: > 0 })
            {
                throw new CryptographicException("Compressed EC public keys are not supported here.");
            }

            return parametersResult;
        }

        private static RSA CreateRsa(RSAParameters parameters)
        {
            var rsa = RSA.Create();

            try
            {
                rsa.ImportParameters(parameters);
                return rsa;
            }
            catch
            {
                rsa.Dispose();
                throw;
            }
        }

        private static ECDsa CreateEcdsa(ECParameters parameters)
        {
            var ecdsa = ECDsa.Create();

            try
            {
                ecdsa.ImportParameters(parameters);
                return ecdsa;
            }
            catch
            {
                ecdsa.Dispose();
                throw;
            }
        }

        /// <summary>Big-endian unsigned integer without the sign byte.</summary>
        private static byte[] Unsigned(ReadOnlySpan<byte> value)
        {
            var start = 0;

            while (start < value.Length - 1 && value[start] == 0)
                start++;

            return value[start..].ToArray();
        }

        /// <summary>Big-endian unsigned integer left-padded (or stripped of leading zeros) to exactly <paramref name="length"/> bytes.</summary>
        private static byte[] Fixed(ReadOnlySpan<byte> value, int length)
        {
            var unsigned = Unsigned(value);

            if (unsigned.Length > length)
                throw new CryptographicException("Integer larger than expected.");

            var result = new byte[length];
            unsigned.CopyTo(result.AsSpan(length - unsigned.Length));
            return result;
        }
    }
}
