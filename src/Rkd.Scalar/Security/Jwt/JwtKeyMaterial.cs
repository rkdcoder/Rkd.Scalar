using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;

namespace Rkd.Scalar.Security.Jwt
{
    /// <summary>
    /// Resolves the signing and validation keys described by <see cref="JwtOptions"/>.
    /// </summary>
    internal sealed class JwtKeyMaterial
    {
        private JwtKeyMaterial(
            SecurityKey? signingKey,
            string? signingAlgorithm,
            IReadOnlyList<SecurityKey> validationKeys)
        {
            SigningKey = signingKey;
            SigningAlgorithm = signingAlgorithm;
            ValidationKeys = validationKeys;
        }

        /// <summary>Key used to sign tokens, or <see langword="null"/> in validation-only setups.</summary>
        public SecurityKey? SigningKey { get; }

        /// <summary>Algorithm used with <see cref="SigningKey"/>.</summary>
        public string? SigningAlgorithm { get; }

        /// <summary>Keys accepted when validating incoming tokens.</summary>
        public IReadOnlyList<SecurityKey> ValidationKeys { get; }

        public static JwtKeyMaterial Create(JwtOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var privatePem = ReadPem(options.PrivateKeyPem, options.PrivateKeyPath, "PrivateKey");
            var publicPem = ReadPem(options.PublicKeyPem, options.PublicKeyPath, "PublicKey");
            var hasSecret = !string.IsNullOrEmpty(options.Secret);
            var hasAsymmetricSigningKey = options.SigningKey is not null || privatePem is not null;
            var hasAuthority = options.HasAuthority;

            if (hasSecret && hasAsymmetricSigningKey)
                throw new InvalidOperationException(
                    "Configure either a JWT Secret (HMAC) or an asymmetric signing key (PrivateKeyPem, PrivateKeyPath or SigningKey), not both.");

            if (hasSecret && options.Secret!.Length < 32)
                throw new InvalidOperationException("JWT secret must be at least 32 characters.");

            if (!hasSecret && !hasAsymmetricSigningKey && publicPem is null &&
                options.ValidationKeys.Count == 0 && !hasAuthority)
                throw new InvalidOperationException(
                    "No JWT key configured. Set Secret (minimum 32 characters), " +
                    "PrivateKeyPem/PrivateKeyPath, PublicKeyPem/PublicKeyPath, SigningKey or Authority.");

            SecurityKey? signingKey =
                options.SigningKey ??
                (privatePem is not null ? (SecurityKey)ImportPem(privatePem, "PrivateKey") : null) ??
                (hasSecret ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret!)) : null);

            var validationKeys = new List<SecurityKey>();

            if (signingKey is not null)
            {
                AssignKeyId(signingKey, options.KeyId);
                validationKeys.Add(signingKey);
            }

            if (publicPem is not null)
            {
                var publicKey = ImportPem(publicPem, "PublicKey");
                AssignKeyId(publicKey, options.KeyId);

                if (!validationKeys.Any(k => k.KeyId is not null && k.KeyId == publicKey.KeyId))
                    validationKeys.Add(publicKey);
            }

            validationKeys.AddRange(options.ValidationKeys);

            var algorithm = signingKey is null
                ? options.Algorithm
                : options.Algorithm ?? InferAlgorithm(signingKey);

            return new JwtKeyMaterial(signingKey, algorithm, validationKeys);
        }

        /// <summary>
        /// Returns the public part of every asymmetric validation key as JSON Web Keys.
        /// Symmetric keys and private parameters are never included.
        /// </summary>
        public IReadOnlyList<JsonWebKey> GetPublicJsonWebKeys(string? signingAlgorithmOverride = null)
        {
            var result = new List<JsonWebKey>();

            foreach (var key in ValidationKeys)
            {
                var jwk = ToPublicJsonWebKey(key);

                if (jwk is null)
                    continue;

                jwk.Use ??= JsonWebKeyUseNames.Sig;

                if (string.IsNullOrEmpty(jwk.Alg))
                {
                    jwk.Alg = ReferenceEquals(key, SigningKey)
                        ? signingAlgorithmOverride ?? SigningAlgorithm ?? InferAlgorithm(key)
                        : signingAlgorithmOverride ?? InferAlgorithm(key);
                }

                if (!result.Any(k => k.Kid == jwk.Kid))
                    result.Add(jwk);
            }

            return result;
        }

        internal static string InferAlgorithm(SecurityKey key) => key switch
        {
            SymmetricSecurityKey => SecurityAlgorithms.HmacSha256,
            RsaSecurityKey => SecurityAlgorithms.RsaSha256,
            ECDsaSecurityKey ec => EcdsaAlgorithm(ec.ECDsa.KeySize),
            X509SecurityKey x509 when x509.PublicKey is ECDsa ec => EcdsaAlgorithm(ec.KeySize),
            JsonWebKey { Alg: { Length: > 0 } alg } => alg,
            JsonWebKey { Kty: JsonWebAlgorithmsKeyTypes.Octet } => SecurityAlgorithms.HmacSha256,
            JsonWebKey { Kty: JsonWebAlgorithmsKeyTypes.EllipticCurve, Crv: var crv } => crv switch
            {
                JsonWebKeyECTypes.P384 => SecurityAlgorithms.EcdsaSha384,
                JsonWebKeyECTypes.P521 => SecurityAlgorithms.EcdsaSha512,
                _ => SecurityAlgorithms.EcdsaSha256
            },
            _ => SecurityAlgorithms.RsaSha256
        };

        private static string EcdsaAlgorithm(int keySize) => keySize switch
        {
            384 => SecurityAlgorithms.EcdsaSha384,
            521 => SecurityAlgorithms.EcdsaSha512,
            _ => SecurityAlgorithms.EcdsaSha256
        };

        private static void AssignKeyId(SecurityKey key, string? keyId)
        {
            if (!string.IsNullOrWhiteSpace(keyId))
            {
                key.KeyId = keyId;
                return;
            }

            if (key.KeyId is null && key is AsymmetricSecurityKey)
            {
                var jwk = ToPublicJsonWebKey(key);

                if (jwk is not null)
                    key.KeyId = Base64UrlEncoder.Encode(jwk.ComputeJwkThumbprint());
            }
        }

        private static JsonWebKey? ToPublicJsonWebKey(SecurityKey key)
        {
            switch (key)
            {
                case RsaSecurityKey rsa:
                {
                    var parameters = rsa.Rsa is not null
                        ? rsa.Rsa.ExportParameters(includePrivateParameters: false)
                        : new RSAParameters { Modulus = rsa.Parameters.Modulus, Exponent = rsa.Parameters.Exponent };

                    return JsonWebKeyConverter.ConvertFromRSASecurityKey(
                        new RsaSecurityKey(parameters) { KeyId = rsa.KeyId });
                }

                case ECDsaSecurityKey ec:
                {
                    var publicEc = ECDsa.Create(ec.ECDsa.ExportParameters(includePrivateParameters: false));

                    return JsonWebKeyConverter.ConvertFromECDsaSecurityKey(
                        new ECDsaSecurityKey(publicEc) { KeyId = ec.KeyId });
                }

                case X509SecurityKey x509:
                    return JsonWebKeyConverter.ConvertFromX509SecurityKey(x509, representAsRsaKey: x509.PublicKey is RSA);

                case JsonWebKey jwk when !jwk.HasPrivateKey && jwk.Kty != JsonWebAlgorithmsKeyTypes.Octet:
                    return jwk;

                default:
                    return null;
            }
        }

        private static string? ReadPem(string? pem, string? path, string optionName)
        {
            if (!string.IsNullOrWhiteSpace(pem) && !string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException(
                    $"Configure either JwtOptions.{optionName}Pem or JwtOptions.{optionName}Path, not both.");

            if (!string.IsNullOrWhiteSpace(path))
            {
                if (!File.Exists(path))
                    throw new InvalidOperationException(
                        $"JWT key file configured in JwtOptions.{optionName}Path was not found: '{path}'.");

                pem = File.ReadAllText(path);
            }

            if (string.IsNullOrWhiteSpace(pem))
                return null;

            // Environment variables frequently carry PEM content with escaped new lines.
            if (!pem.Contains('\n') && pem.Contains("\\n"))
                pem = pem.Replace("\\n", "\n");

            return pem;
        }

        private static AsymmetricSecurityKey ImportPem(string pem, string optionName)
        {
            var rsa = RSA.Create();

            try
            {
                rsa.ImportFromPem(pem);
                return new RsaSecurityKey(rsa);
            }
            catch (Exception ex) when (ex is ArgumentException or CryptographicException)
            {
                rsa.Dispose();
            }

            var ecdsa = ECDsa.Create();

            try
            {
                ecdsa.ImportFromPem(pem);
                return new ECDsaSecurityKey(ecdsa);
            }
            catch (Exception ex) when (ex is ArgumentException or CryptographicException)
            {
                ecdsa.Dispose();
            }

            throw new InvalidOperationException(
                $"JwtOptions.{optionName} does not contain a valid PEM encoded RSA or ECDSA key.");
        }
    }
}
