using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Rkd.Scalar.Security.Jwt
{
    /// <summary>
    /// Adds <c>Authorization: Bearer</c> with a token issued by <see cref="IJwtTokenService"/> to outgoing requests.
    /// The token is cached per named client (outliving the handler rotation of <c>IHttpClientFactory</c>) and renewed
    /// shortly before it expires; requests that already carry an <c>Authorization</c> header are not changed.
    /// </summary>
    internal sealed class RkdJwtTokenHandler : DelegatingHandler
    {
        private readonly RkdJwtTokenCache _cache;

        private readonly string _clientName;

        private readonly IServiceProvider _services;

        private readonly Func<IServiceProvider, ClaimsIdentity> _identityFactory;

        private readonly RkdJwtTokenOptions _options;

        public RkdJwtTokenHandler(
            RkdJwtTokenCache cache,
            string clientName,
            IServiceProvider services,
            Func<IServiceProvider, ClaimsIdentity> identityFactory,
            RkdJwtTokenOptions options)
        {
            _cache = cache;
            _clientName = clientName;
            _services = services;
            _identityFactory = identityFactory;
            _options = options;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization is null)
            {
                if (_options.ForwardIncomingToken && IncomingBearer() is { } incoming)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", incoming);
                }
                else
                {
                    var token = await _cache.GetAsync(_clientName, CreateTokenAsync, _options.RefreshBeforeExpiration, cancellationToken);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
                }
            }

            return await base.SendAsync(request, cancellationToken);
        }

        /// <summary>The Bearer token of the request being handled, if any.</summary>
        private string? IncomingBearer()
        {
            var authorization = _services.GetService<IHttpContextAccessor>()?.HttpContext?.Request.Headers.Authorization.ToString();

            if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return null;

            var token = authorization["Bearer ".Length..].Trim();

            return token.Length == 0 ? null : token;
        }

        private Task<JwtToken> CreateTokenAsync(CancellationToken cancellationToken)
        {
            var tokens = _services.GetService<IJwtTokenService>() ?? throw new InvalidOperationException(
                "AddRkdJwtToken requires an IJwtTokenService: configure a signing key with WithBearerAuth (Secret, " +
                "PrivateKeyPath, SigningKey or WithJwtSigner) or register your own with WithJwtTokenService<T>().");

            return tokens.CreateTokenAsync(_identityFactory(_services), _options.AdditionalClaims, cancellationToken);
        }
    }

    /// <summary>
    /// Service tokens of the named HTTP clients, renewed by only one request at a time.
    /// </summary>
    internal sealed class RkdJwtTokenCache
    {
        private readonly ConcurrentDictionary<string, Slot> _slots = new(StringComparer.Ordinal);

        private readonly TimeProvider _time;

        public RkdJwtTokenCache(TimeProvider? time = null)
        {
            _time = time ?? TimeProvider.System;
        }

        public async Task<JwtToken> GetAsync(
            string clientName,
            Func<CancellationToken, Task<JwtToken>> create,
            TimeSpan refreshBeforeExpiration,
            CancellationToken cancellationToken)
        {
            var slot = _slots.GetOrAdd(clientName, _ => new Slot());

            if (IsValid(slot.Token, refreshBeforeExpiration))
                return slot.Token!;

            await slot.Lock.WaitAsync(cancellationToken);

            try
            {
                if (!IsValid(slot.Token, refreshBeforeExpiration))
                    slot.Token = await create(cancellationToken);

                return slot.Token!;
            }
            finally
            {
                slot.Lock.Release();
            }
        }

        private bool IsValid(JwtToken? token, TimeSpan refreshBeforeExpiration)
        {
            if (token is null)
                return false;

            var lifetime = token.ExpiresAt - token.IssuedAt;
            var margin = refreshBeforeExpiration < lifetime / 2 ? refreshBeforeExpiration : lifetime / 2;

            return _time.GetUtcNow() < token.ExpiresAt - margin;
        }

        private sealed class Slot
        {
            public SemaphoreSlim Lock { get; } = new(1, 1);

            public JwtToken? Token { get; set; }
        }
    }
}
