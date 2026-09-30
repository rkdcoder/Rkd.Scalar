using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    /// <summary>Keys "stored in a database", shared by the resolver of one test application.</summary>
    public sealed class KeyStore
    {
        public ConcurrentDictionary<string, JwtSigningKey> Keys { get; } = new();

        public ConcurrentDictionary<string, int> Lookups { get; } = new();

        public bool Fail { get; set; }
    }

    public sealed class StoreKeyResolver(KeyStore store) : IJwtSigningKeyResolver
    {
        public async Task<IEnumerable<JwtSigningKey>> ResolveAsync(JwtSigningKeyContext context, CancellationToken cancellationToken)
        {
            await Task.Yield();
            store.Lookups.AddOrUpdate(context.KeyId ?? "", 1, (_, count) => count + 1);

            if (store.Fail)
                throw new InvalidOperationException("database down");

            return context.KeyId is not null && store.Keys.TryGetValue(context.KeyId, out var key) ? [key] : [];
        }
    }

    public class JwtKeyResolverTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static ECDsaSecurityKey NewKey(string kid) =>
            new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = kid };

        private static string Token(SecurityKey key, string issuer, string audience = "audience", IDictionary<string, object>? claims = null) =>
            new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = audience,
                Claims = claims ?? new Dictionary<string, object> { ["name"] = "svc-user" },
                Expires = DateTime.UtcNow.AddMinutes(5),
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256)
            });

        private static Task<TestApp> StartAsync(
            KeyStore store,
            Action<RkdScalarBuilder> configure,
            bool problemDetails = true) =>
            TestApp.StartAsync(
                scalar =>
                {
                    configure(scalar);

                    if (problemDetails)
                        scalar.WithProblemDetails();
                },
                app => app.MapGet("/me", (ClaimsPrincipal user) => user.Identity!.Name)
                    .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = RkdScalarAuthenticationSchemes.Bearer }),
                configureBuilder: builder => builder.Services.AddSingleton(store));

        private static async Task<(HttpStatusCode Status, string Body, HttpResponseMessage Response)> GetMeAsync(TestApp app, string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/me");
            request.Headers.Authorization = new("Bearer", token);
            var response = await app.Client.SendAsync(request, Ct);

            return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct), response);
        }

        [Fact]
        public async Task Resolver_ShouldValidateKeysFromTheStoreBoundToTheirIssuer()
        {
            var store = new KeyStore();
            var key = NewKey("k1");
            store.Keys["k1"] = new JwtSigningKey(key, issuer: "svc-a");

            // Resolver only: no Secret, no public key and no Issuer in the options.
            await using var app = await StartAsync(store, scalar => scalar
                .WithJwtSigningKeyResolver<StoreKeyResolver>()
                .WithBearerAuth(new JwtOptions { Audience = "audience" }));

            var ok = await GetMeAsync(app, Token(key, "svc-a"));
            ok.Status.Should().Be(HttpStatusCode.OK);
            ok.Body.Should().Be("svc-user");

            // Same key, another issuer: a service cannot sign on behalf of another one.
            (await GetMeAsync(app, Token(key, "svc-b"))).Status.Should().Be(HttpStatusCode.Unauthorized);

            // Signed by a key that is not in the store.
            (await GetMeAsync(app, Token(NewKey("k1"), "svc-a"))).Status.Should().Be(HttpStatusCode.Unauthorized);

            // Wrong audience is still validated.
            (await GetMeAsync(app, Token(key, "svc-a", audience: "other"))).Status.Should().Be(HttpStatusCode.Unauthorized);

            store.Lookups["k1"].Should().Be(1, "resolved keys are cached");
        }

        [Fact]
        public async Task UnknownKid_ShouldBeLookedUpAgainAfterRotation_AndThrottled()
        {
            var store = new KeyStore();
            var rotated = NewKey("k2");

            await using var app = await StartAsync(store, scalar => scalar
                .WithJwtSigningKeyResolver<StoreKeyResolver>(o => o.UnknownKeyCacheDuration = TimeSpan.FromMinutes(5))
                .WithBearerAuth(TestJwt.Options(o => o.Secret = new string('s', 32))));

            (await GetMeAsync(app, Token(rotated, "issuer"))).Status.Should().Be(HttpStatusCode.Unauthorized);
            (await GetMeAsync(app, Token(rotated, "issuer"))).Status.Should().Be(HttpStatusCode.Unauthorized);
            store.Lookups["k2"].Should().Be(1, "an unknown kid is not looked up again during UnknownKeyCacheDuration");

            await using var rotationApp = await StartAsync(store = new KeyStore(), scalar => scalar
                .WithJwtSigningKeyResolver<StoreKeyResolver>(o => o.UnknownKeyCacheDuration = TimeSpan.Zero)
                .WithBearerAuth(TestJwt.Options(o => o.Secret = new string('s', 32))));

            (await GetMeAsync(rotationApp, Token(rotated, "issuer"))).Status.Should().Be(HttpStatusCode.Unauthorized);

            store.Keys["k2"] = new JwtSigningKey(rotated);   // new key published in the store

            (await GetMeAsync(rotationApp, Token(rotated, "issuer"))).Status.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task StaticKeys_ShouldKeepWorkingWithAResolver_AndResolverFailuresShouldBe401()
        {
            var store = new KeyStore { Fail = true };
            var options = TestJwt.Options(o => o.Secret = new string('s', 32));

            await using var app = await StartAsync(store, scalar => scalar
                .WithBearerAuth(options)
                .WithJwtSigningKeyResolver<StoreKeyResolver>());   // after WithBearerAuth: fine when a key exists

            var hmac = await TestJwt.Service(options).CreateTokenAsync(
                new ClaimsIdentity([new Claim(ClaimTypes.Name, "local")], "test"), cancellationToken: Ct);

            (await GetMeAsync(app, hmac.AccessToken)).Status.Should().Be(HttpStatusCode.OK);
            (await GetMeAsync(app, Token(NewKey("k9"), "issuer"))).Status.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public void ResolverWithoutIssuer_RequiresTheResolverFirst()
        {
            var act = () => TestApp.StartAsync(scalar => scalar
                .WithBearerAuth(new JwtOptions { Audience = "audience" })
                .WithJwtSigningKeyResolver<StoreKeyResolver>()).GetAwaiter().GetResult();

            act.Should().Throw<InvalidOperationException>().WithMessage("*WithJwtSigningKeyResolver*");
        }

        [Fact]
        public void ResolverWithoutIssuer_ShouldStillRequireIssuerForStaticKeys()
        {
            var act = () => TestApp.StartAsync(scalar => scalar
                .WithJwtSigningKeyResolver<StoreKeyResolver>()
                .WithBearerAuth(new JwtOptions { Audience = "audience", Secret = new string('s', 32) })).GetAwaiter().GetResult();

            act.Should().Throw<InvalidOperationException>().WithMessage("*'Issuer' is required*");
        }

        [Fact]
        public async Task ConfigureJwtBearer_ProblemExceptionFailure_ShouldReachThe401Body()
        {
            var options = TestJwt.Options(o => o.Secret = new string('s', 32));

            await using var app = await StartAsync(new KeyStore(), scalar => scalar
                // Before WithBearerAuth on purpose: the order of the calls does not matter.
                .ConfigureJwtBearer(jwt => jwt.Events.OnTokenValidated = context =>
                {
                    if (context.Principal!.FindFirst("env")?.Value != "prod")
                        context.Fail(RkdError.Unauthorized("WRONG_ENVIRONMENT", "The token was issued for another environment."));

                    return Task.CompletedTask;
                })
                .WithBearerAuth(options));

            var service = TestJwt.Service(options);
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "u")], "test");

            var prod = await service.CreateTokenAsync(identity, [new Claim("env", "prod")], Ct);
            (await GetMeAsync(app, prod.AccessToken)).Status.Should().Be(HttpStatusCode.OK);

            var dev = await service.CreateTokenAsync(identity, [new Claim("env", "dev")], Ct);
            var (status, body, response) = await GetMeAsync(app, dev.AccessToken);

            status.Should().Be(HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.Should().NotBeEmpty();
            var problem = JsonDocument.Parse(body).RootElement;
            problem.GetProperty("code").GetString().Should().Be("WRONG_ENVIRONMENT");
            problem.GetProperty("detail").GetString().Should().Be("The token was issued for another environment.");

            // Other failures stay generic.
            var (_, invalidBody, _) = await GetMeAsync(app, dev.AccessToken + "x");
            var invalid = JsonDocument.Parse(invalidBody).RootElement;
            invalid.GetProperty("code").GetString().Should().Be("UNAUTHORIZED");
            invalid.TryGetProperty("detail", out _).Should().BeFalse();
        }

        [Fact]
        public async Task ConfigureJwtBearer_ShouldKeepYourOwnOnChallenge()
        {
            var options = TestJwt.Options(o => o.Secret = new string('s', 32));
            var challenged = false;

            await using var app = await StartAsync(new KeyStore(), scalar => scalar
                .WithBearerAuth(options)
                .ConfigureJwtBearer(jwt =>
                {
                    jwt.Events.OnChallenge = _ =>
                    {
                        challenged = true;
                        return Task.CompletedTask;
                    };
                }));

            (await GetMeAsync(app, "invalid")).Status.Should().Be(HttpStatusCode.Unauthorized);
            challenged.Should().BeTrue();
        }

        [Fact]
        public async Task AddRkdJwtToken_ShouldAuthenticateOutgoingCallsAndReuseTheToken()
        {
            var options = TestJwt.Options(o => o.Secret = new string('s', 32));
            Microsoft.AspNetCore.TestHost.TestServer? server = null;

            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithBearerAuth<TestCredentials, FakeCredentialValidator>(options),
                app =>
                {
                    server = (Microsoft.AspNetCore.TestHost.TestServer)app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
                    app.MapGet("/whoami", (ClaimsPrincipal user) => new
                        {
                            name = user.Identity!.Name,
                            jti = user.FindFirst("jti")?.Value,
                            scope = user.FindFirst("scope")?.Value
                        })
                        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = RkdScalarAuthenticationSchemes.Bearer });
                },
                configureBuilder: builder => builder.Services
                    .AddHttpClient("inventory", c => c.BaseAddress = new Uri("http://localhost"))
                    .ConfigurePrimaryHttpMessageHandler(() => server!.CreateHandler())
                    .AddRkdJwtToken(
                        new ClaimsIdentity([new Claim(ClaimTypes.Name, "ia-core")], "service"),
                        o => o.AdditionalClaims.Add(new Claim("scope", "vectors.read"))));

            var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient("inventory");

            var first = await client.GetFromJsonAsync<JsonElement>("/whoami", Ct);
            var second = await client.GetFromJsonAsync<JsonElement>("/whoami", Ct);

            first.GetProperty("name").GetString().Should().Be("ia-core");
            first.GetProperty("scope").GetString().Should().Be("vectors.read");
            second.GetProperty("jti").GetString().Should().Be(first.GetProperty("jti").GetString(), "the token is cached");
        }

        [Fact]
        public async Task TokenCache_ShouldRenewShortlyBeforeExpiration()
        {
            var time = new ManualTime(DateTimeOffset.UtcNow);
            var cache = new Security.Jwt.RkdJwtTokenCache(time);
            var created = 0;

            Task<JwtToken> Create(CancellationToken _)
            {
                created++;
                var now = time.GetUtcNow();
                return Task.FromResult(new JwtToken($"t{created}", $"{created}", now, now.AddMinutes(10)));
            }

            (await cache.GetAsync("c", Create, TimeSpan.FromMinutes(1), Ct)).AccessToken.Should().Be("t1");

            time.Advance(TimeSpan.FromMinutes(8));
            (await cache.GetAsync("c", Create, TimeSpan.FromMinutes(1), Ct)).AccessToken.Should().Be("t1");

            time.Advance(TimeSpan.FromMinutes(1.5));
            (await cache.GetAsync("c", Create, TimeSpan.FromMinutes(1), Ct)).AccessToken.Should().Be("t2");
        }

        private sealed class ManualTime(DateTimeOffset now) : TimeProvider
        {
            private DateTimeOffset _now = now;

            public override DateTimeOffset GetUtcNow() => _now;

            public void Advance(TimeSpan by) => _now += by;
        }
    }
}
