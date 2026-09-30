using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rkd.Scalar;
using Rkd.Scalar.Security.Jwt;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Authenticates outgoing calls (service to service) with tokens issued by Rkd.Scalar.
    /// </summary>
    public static class RkdJwtHttpClientBuilderExtensions
    {
        /// <summary>
        /// Sends <c>Authorization: Bearer</c> with a token for <paramref name="identity"/>, issued by
        /// <see cref="IJwtTokenService"/> (HMAC, RSA/ECDSA or an async <see cref="IJwtSigner"/> such as a KMS) and reused
        /// until shortly before it expires.
        /// </summary>
        /// <param name="builder">The HTTP client.</param>
        /// <param name="identity">Identity written to the token (e.g. the calling service).</param>
        /// <param name="configure">Renewal margin and extra claims.</param>
        /// <returns>The same builder.</returns>
        /// <example>
        /// <code>
        /// builder.Services.AddHttpClient&lt;VectorStoreClient&gt;(c => c.BaseAddress = new Uri(url))
        ///     .AddRkdJwtToken(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "ia-core")]));
        /// </code>
        /// </example>
        public static IHttpClientBuilder AddRkdJwtToken(
            this IHttpClientBuilder builder,
            ClaimsIdentity identity,
            Action<RkdJwtTokenOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(identity);

            return builder.AddRkdJwtToken(_ => identity, configure);
        }

        /// <summary>
        /// Sends <c>Authorization: Bearer</c> with a token for the identity created by <paramref name="identityFactory"/>
        /// (called each time a token is issued), reused until shortly before it expires.
        /// </summary>
        /// <param name="builder">The HTTP client.</param>
        /// <param name="identityFactory">Creates the identity written to the token.</param>
        /// <param name="configure">Renewal margin and extra claims.</param>
        /// <returns>The same builder.</returns>
        public static IHttpClientBuilder AddRkdJwtToken(
            this IHttpClientBuilder builder,
            Func<IServiceProvider, ClaimsIdentity> identityFactory,
            Action<RkdJwtTokenOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(identityFactory);

            var options = new RkdJwtTokenOptions();
            configure?.Invoke(options);

            if (options.RefreshBeforeExpiration < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(configure), "RefreshBeforeExpiration cannot be negative.");

            builder.Services.TryAddSingleton<RkdJwtTokenCache>();

            if (options.ForwardIncomingToken)
                builder.Services.AddHttpContextAccessor();

            var name = builder.Name;

            return builder.AddHttpMessageHandler(services => new RkdJwtTokenHandler(
                services.GetRequiredService<RkdJwtTokenCache>(), name, services, identityFactory, options));
        }
    }
}
