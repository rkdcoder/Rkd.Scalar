namespace Rkd.Scalar
{
    /// <summary>
    /// Cache settings of <see cref="IJwtSigningKeyResolver"/>.
    /// </summary>
    public sealed class JwtSigningKeyResolverOptions
    {
        /// <summary>How long resolved keys are reused before the resolver is called again. Defaults to 10 minutes.</summary>
        public TimeSpan KeyCacheDuration { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// How long a <c>kid</c> the resolver did not find stays rejected without calling it again, protecting the
        /// key store from tokens with random <c>kid</c> values. Defaults to 30 seconds; <see cref="TimeSpan.Zero"/>
        /// looks it up on every request.
        /// </summary>
        public TimeSpan UnknownKeyCacheDuration { get; set; } = TimeSpan.FromSeconds(30);
    }
}
