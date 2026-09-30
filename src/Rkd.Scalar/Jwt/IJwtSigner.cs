namespace Rkd.Scalar
{
    /// <summary>
    /// Signs JWT tokens asynchronously, typically delegating to a remote key store
    /// (Azure Key Vault, AWS KMS, Google Cloud KMS, an HSM…) where the private key never leaves the vault.
    /// </summary>
    /// <remarks>
    /// Register it with <c>WithJwtSigner&lt;TSigner&gt;()</c>. When registered, the token service
    /// builds the token header and payload and delegates only the signature to this service.
    /// To validate the tokens, configure the matching public key (<see cref="JwtOptions.PublicKeyPem"/>,
    /// <see cref="JwtOptions.PublicKeyPath"/>, <see cref="JwtOptions.ValidationKeys"/>) or an <see cref="JwtOptions.Authority"/>.
    /// </remarks>
    public interface IJwtSigner
    {
        /// <summary>
        /// JWS algorithm written to the token header (for example <c>RS256</c>, <c>PS256</c> or <c>ES256</c>).
        /// </summary>
        string Algorithm { get; }

        /// <summary>
        /// Key identifier written to the token header (<c>kid</c>), or <see langword="null"/> to omit it.
        /// </summary>
        string? KeyId { get; }

        /// <summary>
        /// Signs the JWS signing input (<c>BASE64URL(header) + "." + BASE64URL(payload)</c> as ASCII bytes).
        /// </summary>
        /// <param name="data">Bytes to sign.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>
        /// The raw signature. For ECDSA algorithms it must use the IEEE P1363 format (R || S) required by RFC 7518,
        /// not DER.
        /// </returns>
        Task<byte[]> SignAsync(byte[] data, CancellationToken cancellationToken = default);
    }
}
