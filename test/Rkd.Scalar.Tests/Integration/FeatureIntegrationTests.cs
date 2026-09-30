using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Extensions;
using Rkd.Scalar.Security;
using Rkd.Scalar.Security.Jwt;
using Rkd.Scalar.Tests.Helpers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Rkd.Scalar.Tests.Integration
{
    public class FeatureIntegrationTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static readonly JwtOptions HmacOptions = new()
        {
            Secret = new string('s', 32),
            Issuer = "issuer",
            Audience = "audience",
            Expiration = TimeSpan.FromMinutes(10)
        };

        private static void MapSecure(WebApplication app, string? schemes = null)
        {
            app.MapGet("/secure", (HttpContext ctx) => ctx.User.Identity!.Name ?? "anonymous")
                .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = schemes });

            app.MapGet("/public", () => "ok").AllowAnonymous();
        }

        private static AuthenticationHeaderValue BasicHeader(string user, string password) =>
            new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

        private static async Task<JsonElement> LoginAsync(HttpClient client, string path = "/auth/login")
        {
            var response = await client.PostAsJsonAsync(path, new { username = "user", password = "pass" }, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }

        [Fact]
        public async Task DefaultJwtLogin_ShouldIssueOAuthStyleToken_UsableOnProtectedEndpoint()
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar
                    .WithBearerAuth<TestCredentials, FakeCredentialValidator>(HmacOptions)
                    .WithDefaultJwtLogin<TestCredentials>("/auth/login", 10, TimeSpan.FromMinutes(1)),
                app => MapSecure(app, RkdScalarAuthenticationSchemes.Bearer));

            var body = await LoginAsync(app.Client);

            body.GetProperty("token_type").GetString().Should().Be("Bearer");
            body.GetProperty("expires_in").GetInt64().Should().BeInRange(590, 600);
            body.TryGetProperty("expires_at", out _).Should().BeTrue();

            var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
            request.Headers.Authorization = new("Bearer", body.GetProperty("access_token").GetString());

            var secure = await app.Client.SendAsync(request, Ct);
            secure.StatusCode.Should().Be(HttpStatusCode.OK);
            (await secure.Content.ReadAsStringAsync(Ct)).Should().Be("user");
        }

        [Fact]
        public async Task DefaultJwtLogin_ShouldRejectInvalidCredentials()
        {
            await using var app = await TestApp.StartAsync(scalar => scalar
                .WithBearerAuth<TestCredentials, FakeCredentialValidator>(HmacOptions)
                .WithDefaultJwtLogin<TestCredentials>("/auth/login", 10, TimeSpan.FromMinutes(1)));

            var response = await app.Client.PostAsJsonAsync("/auth/login", new { username = "user", password = "wrong" }, Ct);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task DefaultJwtLogin_ShouldRateLimit()
        {
            await using var app = await TestApp.StartAsync(scalar => scalar
                .WithBearerAuth<TestCredentials, FakeCredentialValidator>(HmacOptions)
                .WithDefaultJwtLogin<TestCredentials>("/auth/login", 2, TimeSpan.FromMinutes(1)));

            await LoginAsync(app.Client);
            await LoginAsync(app.Client);

            var third = await app.Client.PostAsJsonAsync("/auth/login", new { username = "user", password = "pass" }, Ct);

            third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            third.Headers.RetryAfter.Should().NotBeNull();
        }

        [Fact]
        public async Task RsaKeys_ShouldIssueValidateAndPublishJwks()
        {
            var (privatePem, _) = TestKeys.Rsa();

            await using var app = await TestApp.StartAsync(
                scalar => scalar
                    .WithBearerAuth<TestCredentials, FakeCredentialValidator>(new JwtOptions
                    {
                        PrivateKeyPem = privatePem,
                        Issuer = "issuer",
                        Audience = "audience",
                        Algorithm = "RS256"
                    })
                    .WithDefaultJwtLogin<TestCredentials>("/auth/login", 10, TimeSpan.FromMinutes(1))
                    .WithJwksEndpoint(),
                app => MapSecure(app, RkdScalarAuthenticationSchemes.Bearer));

            var token = (await LoginAsync(app.Client)).GetProperty("access_token").GetString();

            var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
            request.Headers.Authorization = new("Bearer", token);
            (await app.Client.SendAsync(request, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

            var jwks = await app.Client.GetStringAsync("/.well-known/jwks.json", Ct);
            using var doc = JsonDocument.Parse(jwks);
            var key = doc.RootElement.GetProperty("keys").EnumerateArray().Single();

            key.GetProperty("kty").GetString().Should().Be("RSA");
            key.GetProperty("alg").GetString().Should().Be("RS256");
            key.TryGetProperty("n", out _).Should().BeTrue();
            key.TryGetProperty("d", out _).Should().BeFalse("private parameters must never be published");
        }

        [Fact]
        public async Task JwksEndpoint_WithHmacSecret_ShouldFailFast()
        {
            var act = () => TestApp.StartAsync(scalar => scalar
                .WithBearerAuth(HmacOptions)
                .WithJwksEndpoint());

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*HMAC secrets are never published*");
        }

        [Fact]
        public async Task JwtSigner_ShouldBeUsedByDefaultLogin()
        {
            var (privatePem, publicPem) = TestKeys.Rsa();

            await using var app = await TestApp.StartAsync(
                scalar =>
                {
                    scalar.Services.AddSingleton(new Unit.FakeKmsKey(privatePem));
                    scalar
                        .WithBearerAuth<TestCredentials, FakeCredentialValidator>(new JwtOptions
                        {
                            PublicKeyPem = publicPem,
                            KeyId = "kms-key-1",
                            Issuer = "issuer",
                            Audience = "audience"
                        })
                        .WithJwtSigner<Unit.FakeKmsSigner>()
                        .WithDefaultJwtLogin<TestCredentials>("/auth/login", 10, TimeSpan.FromMinutes(1));
                },
                app => MapSecure(app, RkdScalarAuthenticationSchemes.Bearer));

            var token = (await LoginAsync(app.Client)).GetProperty("access_token").GetString();

            var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
            request.Headers.Authorization = new("Bearer", token);
            (await app.Client.SendAsync(request, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task JwtOptionsFromConfiguration_ShouldSupportPemAndMinutes()
        {
            var (privatePem, _) = TestKeys.EcP256();

            await using var app = await TestApp.StartAsync(
                scalar => scalar
                    .WithBearerAuth<TestCredentials, FakeCredentialValidator>("Auth:Jwt")
                    .WithDefaultJwtLogin<TestCredentials>("/auth/login", 10, TimeSpan.FromMinutes(1)),
                settings: new Dictionary<string, string?>
                {
                    ["Auth:Jwt:PrivateKeyPem"] = privatePem,
                    ["Auth:Jwt:Issuer"] = "issuer",
                    ["Auth:Jwt:Audience"] = "audience",
                    ["Auth:Jwt:ExpirationMinutes"] = "15"
                });

            var body = await LoginAsync(app.Client);

            body.GetProperty("expires_in").GetInt64().Should().BeInRange(890, 900);
        }

        [Fact]
        public async Task UiProtection_FromConfiguration_ShouldGuardScalarAndOpenApi()
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithUiProtection(),
                settings: new Dictionary<string, string?>
                {
                    ["UiCredentials:alice"] = "wonderland"
                });

            (await app.Client.GetAsync("/scalar/v1", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await app.Client.GetAsync("/openapi/v1.json", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var request = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");
            request.Headers.Authorization = BasicHeader("ALICE", "wonderland");
            (await app.Client.SendAsync(request, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

            var wrong = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");
            wrong.Headers.Authorization = BasicHeader("alice", "WONDERLAND");
            (await app.Client.SendAsync(wrong, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task UiProtection_MissingSection_ShouldFailFast()
        {
            var act = () => TestApp.StartAsync(scalar => scalar.WithUiProtection("Nope"));

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'Nope'*");
        }

        [Fact]
        public async Task CustomRoutes_ShouldBeServedAndProtected()
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithUiProtection("admin", "secret"),
                useScalar: app => app.UseRkdScalar("RkdScalar", o =>
                {
                    o.OpenApiRoutePattern = "/docs/{documentName}/openapi.json";
                    o.ScalarRoutePrefix = "/api-docs";
                }));

            (await app.Client.GetAsync("/api-docs/v1", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await app.Client.GetAsync("/docs/v1/openapi.json", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var ui = new HttpRequestMessage(HttpMethod.Get, "/api-docs/v1");
            ui.Headers.Authorization = BasicHeader("admin", "secret");
            var uiResponse = await app.Client.SendAsync(ui, Ct);
            uiResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            (await uiResponse.Content.ReadAsStringAsync(Ct)).Should().Contain("docs/v1/openapi.json");

            var doc = new HttpRequestMessage(HttpMethod.Get, "/docs/v1/openapi.json");
            doc.Headers.Authorization = BasicHeader("admin", "secret");
            (await app.Client.SendAsync(doc, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task Disabled_ShouldNotMapDocumentation()
        {
            await using var app = await TestApp.StartAsync(
                scalar => { },
                settings: new Dictionary<string, string?> { ["RkdScalar:Enabled"] = "false" });

            (await app.Client.GetAsync("/scalar/v1", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await app.Client.GetAsync("/openapi/v1.json", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task ApiKey_FromConfiguration_WithCustomHeader()
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithApiKeyAuth(configure: o => o.HeaderName = "X-Partner-Key"),
                app => MapSecure(app, RkdScalarAuthenticationSchemes.ApiKey),
                settings: new Dictionary<string, string?>
                {
                    ["ApiKeys:partner-a"] = "key-a",
                    ["ApiKeys:partner-b:Key"] = "key-b",
                    ["ApiKeys:partner-b:Roles:0"] = "reader"
                });

            async Task<HttpResponseMessage> CallAsync(string header, string key)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
                request.Headers.Add(header, key);
                return await app.Client.SendAsync(request, Ct);
            }

            var ok = await CallAsync("X-Partner-Key", "key-b");
            ok.StatusCode.Should().Be(HttpStatusCode.OK);
            (await ok.Content.ReadAsStringAsync(Ct)).Should().Be("partner-b");

            (await CallAsync("X-Partner-Key", "nope")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await CallAsync("X-API-Key", "key-a")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var openApi = await app.Client.GetStringAsync("/openapi/v1.json", Ct);
            openApi.Should().Contain("X-Partner-Key");
        }

        [Fact]
        public async Task DefaultAuthenticationScheme_ShouldAcceptEverySchemeOnPlainAuthorize()
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar
                    .WithBearerAuth<TestCredentials, FakeCredentialValidator>(HmacOptions)
                    .WithDefaultJwtLogin<TestCredentials>("/auth/login", 10, TimeSpan.FromMinutes(1))
                    .WithBasicAuth()
                    .WithApiKeyAuth()
                    .WithDefaultAuthenticationScheme(),
                app => MapSecure(app),
                settings: new Dictionary<string, string?>
                {
                    ["BasicAuth:Username"] = "basic-user",
                    ["BasicAuth:Password"] = "basic-pass",
                    ["ApiKeys:0"] = "key-1"
                });

            var token = (await LoginAsync(app.Client)).GetProperty("access_token").GetString();

            var bearer = new HttpRequestMessage(HttpMethod.Get, "/secure");
            bearer.Headers.Authorization = new("Bearer", token);
            (await app.Client.SendAsync(bearer, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

            var basic = new HttpRequestMessage(HttpMethod.Get, "/secure");
            basic.Headers.Authorization = BasicHeader("basic-user", "basic-pass");
            (await app.Client.SendAsync(basic, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

            var apiKey = new HttpRequestMessage(HttpMethod.Get, "/secure");
            apiKey.Headers.Add("X-API-Key", "key-1");
            (await app.Client.SendAsync(apiKey, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

            (await app.Client.GetAsync("/secure", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task OperationSecurity_ShouldOnlyMarkProtectedEndpoints()
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar
                    .WithBearerAuth(HmacOptions)
                    .WithApiKeyAuth()
                    .WithOperationSecurity(),
                app =>
                {
                    MapSecure(app);
                    app.MapGet("/bearer-only", () => "ok")
                        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = RkdScalarAuthenticationSchemes.Bearer });
                },
                settings: new Dictionary<string, string?> { ["ApiKeys:0"] = "key-1" });

            using var doc = JsonDocument.Parse(await app.Client.GetStringAsync("/openapi/v1.json", Ct));
            var root = doc.RootElement;

            root.TryGetProperty("security", out _).Should().BeFalse("requirements are per operation");
            root.GetProperty("components").GetProperty("securitySchemes").EnumerateObject()
                .Select(p => p.Name).Should().BeEquivalentTo("Bearer", "ApiKey");

            string[] SchemesOf(string path) =>
                root.GetProperty("paths").GetProperty(path).GetProperty("get").TryGetProperty("security", out var security)
                    ? security.EnumerateArray().SelectMany(r => r.EnumerateObject().Select(p => p.Name)).ToArray()
                    : [];

            SchemesOf("/public").Should().BeEmpty();
            SchemesOf("/secure").Should().BeEquivalentTo("Bearer", "ApiKey");
            SchemesOf("/bearer-only").Should().BeEquivalentTo("Bearer");
        }

        [Fact]
        public async Task WithoutOperationSecurity_ShouldKeepGlobalRequirement()
        {
            await using var app = await TestApp.StartAsync(scalar => scalar.WithBearerAuth(HmacOptions));

            using var doc = JsonDocument.Parse(await app.Client.GetStringAsync("/openapi/v1.json", Ct));

            doc.RootElement.GetProperty("security").GetArrayLength().Should().Be(1);
        }
    }
}
