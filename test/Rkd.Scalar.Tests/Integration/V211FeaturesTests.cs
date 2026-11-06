using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Rkd.Scalar.HttpLogging;
using Rkd.Scalar.Security.Jwt;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    [ApiController]
    [Route("v211/reports")]
    [HttpLogBodies(false)]
    public sealed class QuietReportsController : ControllerBase
    {
        [HttpPost]
        public object Create([FromBody] object request) => new { report = "created" };

        [HttpPost("audited")]
        [HttpLogBodies(true)]
        public object Audited([FromBody] object request) => new { report = "audited" };

        [HttpPost("request-only")]
        [HttpLogBodies(RequestOnSuccess = true, ResponseOnSuccess = false)]
        public object RequestOnly([FromBody] object request) => new { report = "request-only" };

        [HttpPost("invalid")]
        public IActionResult Invalid([FromBody] object request) => BadRequest(new { report = "invalid" });
    }

    public class V211FeaturesTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static readonly ClaimsIdentity User = new([new Claim(ClaimTypes.Name, "rodrigo")], "test");

        private static JsonElement Payload(string token) =>
            JsonDocument.Parse(Base64UrlEncoder.Decode(token.Split('.')[1])).RootElement;

        private static JwtOptions Hmac(Action<JwtOptions>? configure = null) => TestJwt.Options(o =>
        {
            o.Secret = new string('s', 32);
            configure?.Invoke(o);
        });

        // ---------- 1. Validity per token ----------

        [Fact]
        public async Task ExpiresAt_ShouldBeWrittenToTheToken_WithoutOutlivingIt()
        {
            var service = TestJwt.Service(Hmac());
            var until = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(7).ToUnixTimeSeconds()).AddMilliseconds(900);

            var token = await service.CreateTokenAsync(User, until, cancellationToken: Ct);
            var payload = Payload(token.AccessToken);

            payload.GetProperty("exp").GetInt64().Should().Be(until.ToUnixTimeSeconds());
            token.ExpiresAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(until.ToUnixTimeSeconds()));
            token.ExpiresAt.Should().BeBefore(until, "exp has whole seconds and never goes past what was asked");
            payload.GetProperty("iat").GetInt64().Should().Be(token.IssuedAt.ToUnixTimeSeconds());
            payload.GetProperty("nbf").GetInt64().Should().Be(token.IssuedAt.ToUnixTimeSeconds());
            payload.GetProperty("name").GetString().Should().Be("rodrigo");
            payload.GetProperty("jti").GetString().Should().Be(token.TokenId);
        }

        [Fact]
        public async Task Lifetime_ShouldCountFromTheMomentOfIssue()
        {
            var token = await TestJwt.Service(Hmac()).CreateTokenAsync(User, TimeSpan.FromSeconds(90), [new Claim("scope", "read")], Ct);

            (token.ExpiresAt - token.IssuedAt).Should().Be(TimeSpan.FromSeconds(90));
            Payload(token.AccessToken).GetProperty("exp").GetInt64().Should().Be(token.IssuedAt.ToUnixTimeSeconds() + 90);
            Payload(token.AccessToken).GetProperty("scope").GetString().Should().Be("read");
        }

        [Fact]
        public async Task CallsWithoutValidity_ShouldKeepTheConfiguredExpiration()
        {
            var service = TestJwt.Service(Hmac(o => o.ExpirationInMinutes = 30));

            var named = await service.CreateTokenAsync(User, cancellationToken: Ct);
            var positional = await service.CreateTokenAsync(User, [new Claim("scope", "read")], Ct);
            var nullClaims = await service.CreateTokenAsync(User, null, Ct);
            var viaInterface = await ((IJwtTokenService)service).CreateTokenAsync(User, cancellationToken: Ct);

            foreach (var token in new[] { named, positional, nullClaims, viaInterface })
                (token.ExpiresAt - token.IssuedAt).Should().Be(TimeSpan.FromMinutes(30));
        }

        [Fact]
        public async Task ExpClaim_ShouldNeverOverrideTheValidity()
        {
            var token = await TestJwt.Service(Hmac()).CreateTokenAsync(
                new ClaimsIdentity([new Claim("exp", "99999999999")]),
                TimeSpan.FromMinutes(1),
                [new Claim("exp", "99999999998")],
                Ct);

            Payload(token.AccessToken).GetProperty("exp").GetInt64().Should().Be(token.ExpiresAt.ToUnixTimeSeconds());
            (token.ExpiresAt - token.IssuedAt).Should().Be(TimeSpan.FromMinutes(1));
        }

        [Fact]
        public async Task ExpiresAt_NotAfterTheMomentOfIssue_ShouldBeRefused()
        {
            var service = TestJwt.Service(Hmac());

            await FluentActions.Awaiting(() => service.CreateTokenAsync(User, DateTimeOffset.UtcNow.AddMinutes(-1), cancellationToken: Ct))
                .Should().ThrowAsync<ArgumentOutOfRangeException>();
            await FluentActions.Awaiting(() => service.CreateTokenAsync(User, default(DateTimeOffset), cancellationToken: Ct))
                .Should().ThrowAsync<ArgumentOutOfRangeException>();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1000)]
        [InlineData(500)]
        public async Task Lifetime_ShorterThanOneSecond_ShouldBeRefused(int milliseconds)
        {
            var service = TestJwt.Service(Hmac());

            await FluentActions.Awaiting(() => service.CreateTokenAsync(User, TimeSpan.FromMilliseconds(milliseconds), cancellationToken: Ct))
                .Should().ThrowAsync<ArgumentOutOfRangeException>();
        }

        [Fact]
        public async Task AsyncSigner_ShouldSignTheTokenWithItsOwnValidity()
        {
            var signer = new HmacSigner();
            var options = Hmac();

            var token = await new JwtTokenService(options, JwtKeyMaterial.Create(options), signer)
                .CreateTokenAsync(User, TimeSpan.FromMinutes(2), cancellationToken: Ct);

            signer.Calls.Should().Be(1);
            (token.ExpiresAt - token.IssuedAt).Should().Be(TimeSpan.FromMinutes(2));

            var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.AccessToken, new TokenValidationParameters
            {
                ValidIssuer = "issuer",
                ValidAudience = "audience",
                IssuerSigningKey = new SymmetricSecurityKey(HmacSigner.Key)
            });

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task Api_ShouldApplyTheValidityOfTheToken_NotTheConfiguredOne()
        {
            var options = Hmac(o => { o.ClockSkewInSeconds = 0; o.ExpirationInMinutes = 60; });

            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithBearerAuth<TestCredentials, FakeCredentialValidator>(options),
                app => app.MapGet("/me", (ClaimsPrincipal user) => user.Identity!.Name)
                    .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = RkdScalarAuthenticationSchemes.Bearer }));

            async Task<HttpStatusCode> GetMeAsync(JwtToken token)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "/me");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
                return (await app.Client.SendAsync(request, Ct)).StatusCode;
            }

            // Through the interface, as an application calls it.
            var tokens = app.Services.GetRequiredService<IJwtTokenService>();
            (await GetMeAsync(await tokens.CreateTokenAsync(User, DateTimeOffset.UtcNow.AddMinutes(5), cancellationToken: Ct))).Should().Be(HttpStatusCode.OK);
            (await GetMeAsync(await tokens.CreateTokenAsync(User, TimeSpan.FromMinutes(5), cancellationToken: Ct))).Should().Be(HttpStatusCode.OK);

            // Issued two minutes ago: one minute of validity is over, the configured sixty are not.
            IJwtTokenService issuedEarlier = new JwtTokenService(options, JwtKeyMaterial.Create(options), time: new FixedTime(DateTimeOffset.UtcNow.AddMinutes(-2)));
            (await GetMeAsync(await issuedEarlier.CreateTokenAsync(User, TimeSpan.FromMinutes(1), cancellationToken: Ct))).Should().Be(HttpStatusCode.Unauthorized);
            (await GetMeAsync(await issuedEarlier.CreateTokenAsync(User, cancellationToken: Ct))).Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task CustomService_WithoutSupport_ShouldFail_InsteadOfIssuingAnotherValidity()
        {
            IJwtTokenService legacy = new LegacyTokenService();

            (await legacy.CreateTokenAsync(User, cancellationToken: Ct)).AccessToken.Should().Be("legacy", "the old contract keeps working");

            await FluentActions.Awaiting(() => legacy.CreateTokenAsync(User, DateTimeOffset.UtcNow.AddMinutes(1), cancellationToken: Ct))
                .Should().ThrowAsync<NotSupportedException>().WithMessage($"*{nameof(LegacyTokenService)}*");
            await FluentActions.Awaiting(() => legacy.CreateTokenAsync(User, TimeSpan.FromMinutes(1), cancellationToken: Ct))
                .Should().ThrowAsync<NotSupportedException>();
        }

        [Fact]
        public async Task CustomService_WithTheInstant_ShouldGetTheLifetimeForFree()
        {
            IJwtTokenService service = new InstantTokenService();
            var before = DateTimeOffset.UtcNow;

            var token = await service.CreateTokenAsync(User, TimeSpan.FromMinutes(3), cancellationToken: Ct);

            token.ExpiresAt.Should().BeOnOrAfter(before.AddMinutes(3)).And.BeOnOrBefore(DateTimeOffset.UtcNow.AddMinutes(3));

            await FluentActions.Awaiting(() => service.CreateTokenAsync(User, TimeSpan.Zero, cancellationToken: Ct))
                .Should().ThrowAsync<ArgumentOutOfRangeException>();
        }

        [Fact]
        public async Task LoginResponse_ShouldReportTheValidityOfTheToken()
        {
            var token = await TestJwt.Service(Hmac()).CreateTokenAsync(User, TimeSpan.FromSeconds(90), cancellationToken: Ct);

            var response = JwtLoginResponse.FromToken(token);

            response.ExpiresIn.Should().BeInRange(85, 90);
            response.ExpiresAt.Should().Be(token.ExpiresAt);
        }

        [Fact]
        public async Task ServiceTokens_ShouldUseTheLifetimeOfTheClient()
        {
            var options = Hmac(o => o.ExpirationInMinutes = 60);
            TestServer? server = null;

            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithBearerAuth<TestCredentials, FakeCredentialValidator>(options),
                app =>
                {
                    server = (TestServer)app.Services.GetRequiredService<IServer>();
                    app.MapGet("/validity", (ClaimsPrincipal user) => long.Parse(user.FindFirst("exp")!.Value) - long.Parse(user.FindFirst("iat")!.Value))
                        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = RkdScalarAuthenticationSchemes.Bearer });
                },
                configureBuilder: builder =>
                {
                    builder.Services.AddHttpClient("short", c => c.BaseAddress = new Uri("http://localhost"))
                        .ConfigurePrimaryHttpMessageHandler(() => server!.CreateHandler())
                        .AddRkdJwtToken(User, o => o.Lifetime = TimeSpan.FromMinutes(5));

                    builder.Services.AddHttpClient("configured", c => c.BaseAddress = new Uri("http://localhost"))
                        .ConfigurePrimaryHttpMessageHandler(() => server!.CreateHandler())
                        .AddRkdJwtToken(User);
                });

            var clients = app.Services.GetRequiredService<IHttpClientFactory>();

            (await clients.CreateClient("short").GetFromJsonAsync<long>("/validity", Ct)).Should().Be(300);
            (await clients.CreateClient("configured").GetFromJsonAsync<long>("/validity", Ct)).Should().Be(3600);
        }

        [Fact]
        public void ServiceTokens_WithInvalidLifetime_ShouldFailAtStartup()
        {
            var act = () => new ServiceCollection().AddHttpClient("x").AddRkdJwtToken(User, o => o.Lifetime = TimeSpan.FromMilliseconds(100));

            act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Lifetime*");
        }

        // ---------- 2. Bodies only of failed requests ----------

        public sealed record Item(int Id, string Name, string? Password = null);

        private static async Task<(TestApp App, MemoryHttpLogSink Sink)> StartLogAsync(
            Action<RkdHttpLoggingOptions>? configure = null,
            Action<WebApplication>? endpoints = null,
            IDictionary<string, string?>? settings = null)
        {
            var sink = new MemoryHttpLogSink();

            var app = await TestApp.StartAsync(
                scalar =>
                {
                    scalar.Services.AddControllers().AddApplicationPart(typeof(QuietReportsController).Assembly);
                    scalar.WithProblemDetails();
                    scalar.WithHttpLogSink(_ => sink);

                    if (configure is not null)
                        scalar.WithHttpLogging(configure);
                },
                app =>
                {
                    app.MapControllers();
                    app.MapPost("/items", (Item item) => Results.Created($"/items/{item.Id}", item));
                    app.MapPost("/bad", (Item item) => Results.BadRequest(new { error = "invalid", item.Name }));
                    app.MapPost("/reject", string (Item item) => throw RkdError.Conflict("ITEM_EXISTS", "Already exists."));
                    app.MapPost("/fail", string (Item item) => throw new InvalidOperationException("kaboom"));
                    endpoints?.Invoke(app);
                },
                settings: settings);

            return (app, sink);
        }

        private static void OnlyFailures(RkdHttpLoggingOptions options)
        {
            options.LogRequestBodyOnSuccess = false;
            options.LogResponseBodyOnSuccess = false;
        }

        private static Item Table => new(7, "table");

        [Fact]
        public void Options_ShouldKeepTheBehaviorOfBefore_ByDefault()
        {
            var options = new RkdHttpLoggingOptions();

            options.LogRequestBodyOnSuccess.Should().BeNull();
            options.LogResponseBodyOnSuccess.Should().BeNull();
            options.KeepBodiesWhen.Should().BeNull();
            options.CaptureRequestBody.Should().BeTrue();
            options.CaptureResponseBody.Should().BeTrue();
        }

        [Theory]
        [InlineData(null)]
        [InlineData(true)]
        public async Task NullOrTrue_ShouldKeepTheBodiesOfRequestsThatSucceeded(bool? value)
        {
            var (app, sink) = await StartLogAsync(o =>
            {
                o.LogRequestBodyOnSuccess = value;
                o.LogResponseBodyOnSuccess = value;
            });
            await using var _ = app;

            await app.Client.PostAsJsonAsync("/items", Table, Ct);

            var entry = await sink.WaitForAsync(e => e.Path == "/items");
            entry.RequestBody.Should().Contain("\"name\":\"table\"");
            entry.ResponseBody.Should().Contain("\"id\":7");
        }

        [Fact]
        public async Task False_ShouldWriteTheEntryOfARequestThatSucceeded_WithoutTheBodies()
        {
            var (app, sink) = await StartLogAsync(OnlyFailures);
            await using var _ = app;

            var response = await app.Client.PostAsJsonAsync("/items", Table, Ct);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("\"name\":\"table\"", "the client gets everything");

            var entry = await sink.WaitForAsync(e => e.Path == "/items");
            entry.IsSuccess.Should().BeTrue();
            entry.StatusCode.Should().Be(201);
            entry.RequestBody.Should().BeNull();
            entry.ResponseBody.Should().BeNull();
            entry.RequestBodyTruncated.Should().BeFalse();
            entry.ResponseBodyTruncated.Should().BeFalse();
            entry.ResponseSize.Should().BePositive("sizes and content types stay in the entry");
            entry.ResponseContentType.Should().StartWith("application/json");
            entry.RequestContentType.Should().StartWith("application/json");
        }

        [Fact]
        public async Task EachBody_ShouldBeChosenApart()
        {
            var (requestOff, requestOffSink) = await StartLogAsync(o => o.LogRequestBodyOnSuccess = false);
            await using (requestOff)
            {
                await requestOff.Client.PostAsJsonAsync("/items", Table, Ct);

                var entry = await requestOffSink.WaitForAsync(e => e.Path == "/items");
                entry.RequestBody.Should().BeNull();
                entry.ResponseBody.Should().Contain("\"id\":7");
            }

            var (responseOff, responseOffSink) = await StartLogAsync(o => o.LogResponseBodyOnSuccess = false);
            await using (responseOff)
            {
                await responseOff.Client.PostAsJsonAsync("/items", Table, Ct);

                var entry = await responseOffSink.WaitForAsync(e => e.Path == "/items");
                entry.RequestBody.Should().Contain("\"name\":\"table\"");
                entry.ResponseBody.Should().BeNull();
            }
        }

        [Theory]
        [InlineData("/bad", 400)]
        [InlineData("/reject", 409)]
        [InlineData("/fail", 500)]
        public async Task False_ShouldKeepBothBodiesOfFailedRequests(string path, int status)
        {
            var (app, sink) = await StartLogAsync(OnlyFailures);
            await using var _ = app;

            (await app.Client.PostAsJsonAsync(path, Table, Ct)).StatusCode.Should().Be((HttpStatusCode)status);

            var entry = await sink.WaitForAsync(e => e.Path == path);
            entry.StatusCode.Should().Be(status);
            entry.IsSuccess.Should().BeFalse();
            entry.RequestBody.Should().Contain("\"name\":\"table\"");
            entry.ResponseBody.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task False_ShouldKeepTheResponseOfAFailureWithoutRequestBody()
        {
            var (app, sink) = await StartLogAsync(OnlyFailures);
            await using var _ = app;

            (await app.Client.GetAsync("/nowhere", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

            var entry = await sink.WaitForAsync(e => e.Path == "/nowhere");
            entry.RequestBody.Should().BeNull();
            entry.ResponseBody.Should().Contain("\"traceId\"");
        }

        [Fact]
        public async Task False_ShouldKeepTheBodyOfAnExceptionAfterTheResponseStarted()
        {
            var (app, sink) = await StartLogAsync(OnlyFailures, app => app.MapGet("/partial", async (HttpContext context) =>
            {
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("partial");
                await context.Response.Body.FlushAsync();
                throw new InvalidOperationException("mid-stream");
            }));
            await using var _ = app;

            try
            {
                using var response = await app.Client.GetAsync("/partial", Ct);
                await response.Content.ReadAsStringAsync(Ct);
            }
            catch (Exception)
            {
                // The response cannot be finished: the client sees a broken stream.
            }

            var entry = await sink.WaitForAsync(e => e.Path == "/partial");
            entry.StatusCode.Should().Be(200, "the status was already sent");
            entry.Exception.Should().Contain("mid-stream");
            entry.ResponseBody.Should().Be("partial");
        }

        [Fact]
        public async Task BodiesOfFailures_ShouldRespectMaxBodyBytes()
        {
            var (app, sink) = await StartLogAsync(o =>
            {
                OnlyFailures(o);
                o.MaxBodyBytes = 10;
            });
            await using var _ = app;

            await app.Client.PostAsJsonAsync("/bad", Table, Ct);

            var entry = await sink.WaitForAsync(e => e.Path == "/bad");
            entry.RequestBody.Should().HaveLength(10);
            entry.RequestBodyTruncated.Should().BeTrue();
            entry.ResponseBody.Should().HaveLength(10);
            entry.ResponseBodyTruncated.Should().BeTrue();
        }

        [Fact]
        public async Task KeepBodiesWhen_ShouldReplaceTheDefault_AndSeeTheEntryWithoutBodies()
        {
            var seenWithBody = false;

            var (app, sink) = await StartLogAsync(o =>
            {
                OnlyFailures(o);
                o.KeepBodiesWhen = (context, entry) =>
                {
                    seenWithBody |= entry.RequestBody is not null || entry.ResponseBody is not null;
                    return entry.StatusCode >= 500 || context.Request.Headers.ContainsKey("X-Audit");
                };
            });
            await using var _ = app;

            await app.Client.PostAsJsonAsync("/bad", Table, Ct);
            await app.Client.PostAsJsonAsync("/fail", Table, Ct);

            var audited = new HttpRequestMessage(HttpMethod.Post, "/items") { Content = JsonContent.Create(Table) };
            audited.Headers.Add("X-Audit", "1");
            await app.Client.SendAsync(audited, Ct);

            var bad = await sink.WaitForAsync(e => e.Path == "/bad");
            bad.RequestBody.Should().BeNull("a 400 is not a failure for this application");
            bad.ResponseBody.Should().BeNull();

            var fail = await sink.WaitForAsync(e => e.Path == "/fail");
            fail.RequestBody.Should().Contain("\"name\":\"table\"");
            fail.ResponseBody.Should().Contain("\"traceId\"");

            var items = await sink.WaitForAsync(e => e.Path == "/items");
            items.RequestBody.Should().Contain("\"name\":\"table\"", "any request can keep them, not only failures");
            items.ResponseBody.Should().Contain("\"id\":7");

            seenWithBody.Should().BeFalse();
        }

        [Fact]
        public async Task KeepBodiesWhen_ShouldNotRun_WhileTheBodiesAreKeptAnyway()
        {
            var calls = 0;

            var (app, sink) = await StartLogAsync(o => o.KeepBodiesWhen = (_, _) =>
            {
                calls++;
                return false;
            });
            await using var _ = app;

            await app.Client.PostAsJsonAsync("/bad", Table, Ct);

            (await sink.WaitForAsync(e => e.Path == "/bad")).RequestBody.Should().NotBeNull();
            calls.Should().Be(0);
        }

        [Fact]
        public async Task KeepBodiesWhen_ThatFails_ShouldNotAffectTheRequest()
        {
            var (app, _) = await StartLogAsync(o =>
            {
                OnlyFailures(o);
                o.KeepBodiesWhen = (_, _) => throw new InvalidOperationException("bug in the application");
            });
            await using var __ = app;

            (await app.Client.PostAsJsonAsync("/items", Table, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        [Fact]
        public async Task CaptureOff_ShouldWin_EvenOnFailures()
        {
            var (app, sink) = await StartLogAsync(o =>
            {
                OnlyFailures(o);
                o.CaptureRequestBody = false;
            });
            await using var _ = app;

            await app.Client.PostAsJsonAsync("/bad", Table, Ct);

            var entry = await sink.WaitForAsync(e => e.Path == "/bad");
            entry.RequestBody.Should().BeNull("CaptureRequestBody = false means never");
            entry.ResponseBody.Should().Contain("invalid");
        }

        [Fact]
        public async Task SensitiveRoutes_ShouldNeverStoreBodies_WithAnyCombination()
        {
            var (app, sink) = await StartLogAsync(
                o =>
                {
                    OnlyFailures(o);
                    o.SensitivePaths.Add("/items");
                    o.SensitivePaths.Add("/bad");
                },
                app =>
                {
                    app.MapPost("/marked/{ok:bool}", (bool ok, Item item) => ok ? Results.Ok(item) : Results.BadRequest(item))
                        .WithSensitiveHttpLog();
                    app.MapPost("/marked-loud", (Item item) => Results.Ok(item))
                        .WithSensitiveHttpLog()
                        .WithHttpLogBodies(onSuccess: true);
                });
            await using var _ = app;

            var secret = new Item(1, "x", Password: "p@ss");

            await app.Client.PostAsJsonAsync("/items", secret, Ct);
            await app.Client.PostAsJsonAsync("/bad", secret, Ct);
            await app.Client.PostAsJsonAsync("/marked/true", secret, Ct);
            await app.Client.PostAsJsonAsync("/marked/false", secret, Ct);
            await app.Client.PostAsJsonAsync("/marked-loud", secret, Ct);

            var items = await sink.WaitForAsync(e => e.Path == "/items");
            items.RequestBody.Should().BeNull();
            items.ResponseBody.Should().BeNull();

            var bad = await sink.WaitForAsync(e => e.Path == "/bad");
            bad.RequestBody.Should().Be("[REDACTED]");
            bad.ResponseBody.Should().Be("[REDACTED]");

            var markedOk = await sink.WaitForAsync(e => e.Path == "/marked/true");
            markedOk.RequestBody.Should().BeNull();
            markedOk.ResponseBody.Should().BeNull();

            var markedFailed = await sink.WaitForAsync(e => e.Path == "/marked/false");
            markedFailed.RequestBody.Should().Be("[REDACTED]");
            markedFailed.ResponseBody.Should().Be("[REDACTED]");

            var loud = await sink.WaitForAsync(e => e.Path == "/marked-loud");
            loud.RequestBody.Should().Be("[REDACTED]", "sensitive wins over the choice of the endpoint");
            loud.ResponseBody.Should().Be("[REDACTED]");
        }

        [Fact]
        public async Task SensitiveRoutes_WithoutTheNewOptions_ShouldStayAsBefore()
        {
            var (app, sink) = await StartLogAsync(o => o.SensitivePaths.Add("/items"));
            await using var _ = app;

            await app.Client.PostAsJsonAsync("/items", new Item(1, "x", Password: "p@ss"), Ct);

            var entry = await sink.WaitForAsync(e => e.Path == "/items");
            entry.RequestBody.Should().Be("[REDACTED]");
            entry.ResponseBody.Should().Be("[REDACTED]");
        }

        [Fact]
        public async Task MinimalEndpoints_ShouldOverrideTheOptions()
        {
            Action<WebApplication> endpoints = app =>
            {
                app.MapPost("/quiet", (Item item) => Results.Ok(item)).WithHttpLogBodies(onSuccess: false);
                app.MapPost("/quiet-failed", (Item item) => Results.BadRequest(item)).WithHttpLogBodies(onSuccess: false);
                app.MapPost("/loud", (Item item) => Results.Ok(item)).WithHttpLogBodies(onSuccess: true);
                app.MapPost("/request-only", (Item item) => Results.Ok(item)).WithHttpLogBodies(requestOnSuccess: true, responseOnSuccess: null);
                app.MapPost("/neither-set", (Item item) => Results.Ok(item)).WithHttpLogBodies(requestOnSuccess: null, responseOnSuccess: null);
                app.MapGroup("/group").WithHttpLogBodies(onSuccess: false).MapPost("/one", (Item item) => Results.Ok(item));
            };

            // The options keep the bodies: the endpoints that say no are the exception.
            var (keeping, keepingSink) = await StartLogAsync(endpoints: endpoints);
            await using (keeping)
            {
                foreach (var path in new[] { "/quiet", "/quiet-failed", "/group/one", "/neither-set" })
                    await keeping.Client.PostAsJsonAsync(path, Table, Ct);

                var quiet = await keepingSink.WaitForAsync(e => e.Path == "/quiet");
                quiet.RequestBody.Should().BeNull();
                quiet.ResponseBody.Should().BeNull();

                var failed = await keepingSink.WaitForAsync(e => e.Path == "/quiet-failed");
                failed.RequestBody.Should().Contain("table");
                failed.ResponseBody.Should().Contain("table");

                var group = await keepingSink.WaitForAsync(e => e.Path == "/group/one");
                group.RequestBody.Should().BeNull();
                group.ResponseBody.Should().BeNull();

                var neither = await keepingSink.WaitForAsync(e => e.Path == "/neither-set");
                neither.RequestBody.Should().Contain("table");
                neither.ResponseBody.Should().Contain("table");
            }

            // The options drop the bodies: the endpoints that say yes are the exception.
            var (dropping, droppingSink) = await StartLogAsync(OnlyFailures, endpoints);
            await using (dropping)
            {
                foreach (var path in new[] { "/loud", "/request-only", "/neither-set" })
                    await dropping.Client.PostAsJsonAsync(path, Table, Ct);

                var loud = await droppingSink.WaitForAsync(e => e.Path == "/loud");
                loud.RequestBody.Should().Contain("table");
                loud.ResponseBody.Should().Contain("table");

                var requestOnly = await droppingSink.WaitForAsync(e => e.Path == "/request-only");
                requestOnly.RequestBody.Should().Contain("table");
                requestOnly.ResponseBody.Should().BeNull("null follows the options");

                var neither = await droppingSink.WaitForAsync(e => e.Path == "/neither-set");
                neither.RequestBody.Should().BeNull();
                neither.ResponseBody.Should().BeNull();
            }
        }

        [Fact]
        public async Task Controllers_ShouldOverrideTheOptions_MostSpecificFirst()
        {
            var (app, sink) = await StartLogAsync();
            await using var _ = app;

            foreach (var path in new[] { "/v211/reports", "/v211/reports/audited", "/v211/reports/request-only", "/v211/reports/invalid" })
                await app.Client.PostAsJsonAsync(path, new { name = "report-request" }, Ct);

            var fromController = await sink.WaitForAsync(e => e.Path == "/v211/reports");
            fromController.RequestBody.Should().BeNull();
            fromController.ResponseBody.Should().BeNull();

            var audited = await sink.WaitForAsync(e => e.Path == "/v211/reports/audited");
            audited.RequestBody.Should().Contain("report-request");
            audited.ResponseBody.Should().Contain("audited");

            var requestOnly = await sink.WaitForAsync(e => e.Path == "/v211/reports/request-only");
            requestOnly.RequestBody.Should().Contain("report-request");
            requestOnly.ResponseBody.Should().BeNull();

            var invalid = await sink.WaitForAsync(e => e.Path == "/v211/reports/invalid");
            invalid.StatusCode.Should().Be(400);
            invalid.RequestBody.Should().Contain("report-request");
            invalid.ResponseBody.Should().Contain("invalid");
        }

        [Fact]
        public void Attribute_ShouldLeaveWhatWasNotSetToTheOptions()
        {
            IHttpLogBodiesMetadata empty = new HttpLogBodiesAttribute();
            empty.LogRequestBodyOnSuccess.Should().BeNull();
            empty.LogResponseBodyOnSuccess.Should().BeNull();

            IHttpLogBodiesMetadata onlyResponse = new HttpLogBodiesAttribute { ResponseOnSuccess = false };
            onlyResponse.LogRequestBodyOnSuccess.Should().BeNull();
            onlyResponse.LogResponseBodyOnSuccess.Should().BeFalse();

            IHttpLogBodiesMetadata both = new HttpLogBodiesAttribute(onSuccess: false);
            both.LogRequestBodyOnSuccess.Should().BeFalse();
            both.LogResponseBodyOnSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task FilterAndEnrich_ShouldWorkWithTheOptions()
        {
            var (app, sink) = await StartLogAsync(o =>
            {
                OnlyFailures(o);
                o.Filter = context => context.Response.StatusCode >= 400;
                o.Enrich = (_, entry) => entry.Properties["hasBody"] = entry.RequestBody is not null;
            });
            await using var _ = app;

            await app.Client.PostAsJsonAsync("/items", Table, Ct);
            await app.Client.PostAsJsonAsync("/bad", Table, Ct);

            var entry = await sink.WaitForAsync(e => e.Path == "/bad");
            entry.Properties["hasBody"].Should().Be(true, "Enrich runs after the decision");
            sink.Entries.Should().NotContain(e => e.Path == "/items");
        }

        [Fact]
        public async Task StreamedResponse_ShouldReachTheClientUnchanged_WithoutTheBody()
        {
            var (app, sink) = await StartLogAsync(OnlyFailures, app => app.MapGet("/stream", async (HttpContext context) =>
            {
                context.Response.ContentType = "text/event-stream";

                for (var i = 0; i < 3; i++)
                {
                    await context.Response.BodyWriter.WriteAsync(Encoding.UTF8.GetBytes($"data: {i}\n\n"));
                    await context.Response.BodyWriter.FlushAsync();
                }
            }));
            await using var _ = app;

            (await app.Client.GetStringAsync("/stream", Ct)).Should().Be("data: 0\n\ndata: 1\n\ndata: 2\n\n");

            var entry = await sink.WaitForAsync(e => e.Path == "/stream");
            entry.ResponseBody.Should().BeNull();
            entry.ResponseSize.Should().Be(27);
        }

        [Fact]
        public async Task ConfigurationSection_ShouldTurnTheBodiesOff()
        {
            var (app, sink) = await StartLogAsync(settings: new Dictionary<string, string?>
            {
                ["HttpLogging:LogRequestBodyOnSuccess"] = "false",
                ["HttpLogging:LogResponseBodyOnSuccess"] = "false"
            });
            await using var _ = app;

            await app.Client.PostAsJsonAsync("/items", Table, Ct);
            await app.Client.PostAsJsonAsync("/bad", Table, Ct);

            var items = await sink.WaitForAsync(e => e.Path == "/items");
            items.RequestBody.Should().BeNull();
            items.ResponseBody.Should().BeNull();

            (await sink.WaitForAsync(e => e.Path == "/bad")).RequestBody.Should().Contain("table");
        }

        [Fact]
        public async Task ConfigurationSection_WithNull_ShouldKeepTheBodies()
        {
            var (app, sink) = await StartLogAsync(settings: new Dictionary<string, string?>
            {
                ["HttpLogging:LogRequestBodyOnSuccess"] = null,
                ["HttpLogging:LogResponseBodyOnSuccess"] = null,
                ["HttpLogging:MaxBodyBytes"] = "1000"
            });
            await using var _ = app;

            await app.Client.PostAsJsonAsync("/items", Table, Ct);

            var entry = await sink.WaitForAsync(e => e.Path == "/items");
            entry.RequestBody.Should().Contain("table");
            entry.ResponseBody.Should().Contain("table");
        }

        /// <summary>A real appsettings file, read by the package: JSON null does not reach the binder as a missing key.</summary>
        private static async Task<RkdHttpLoggingOptions> OptionsFromJsonAsync(string httpLogging)
        {
            var json = $$"""{ "HttpLogging": {{httpLogging}} }""";

            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithHttpLogSink(_ => new MemoryHttpLogSink()),
                configureBuilder: builder => builder.Configuration.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))));

            return app.Services.GetRequiredService<RkdHttpLoggingOptions>();
        }

        [Theory]
        [InlineData("""{ "LogRequestBodyOnSuccess": null, "LogResponseBodyOnSuccess": null }""", null, null)]
        [InlineData("""{ "MaxBodyBytes": 100 }""", null, null)]
        [InlineData("""{ "LogRequestBodyOnSuccess": true, "LogResponseBodyOnSuccess": false }""", true, false)]
        [InlineData("""{ "LogRequestBodyOnSuccess": false, "LogResponseBodyOnSuccess": true }""", false, true)]
        public async Task JsonConfiguration_ShouldBindTheNullableOptions(string json, bool? request, bool? response)
        {
            var options = await OptionsFromJsonAsync(json);

            options.LogRequestBodyOnSuccess.Should().Be(request);
            options.LogResponseBodyOnSuccess.Should().Be(response);
        }

        [Fact]
        public async Task JsonConfiguration_WithNullInThePlainOptions_ShouldKeepTheirDefaults()
        {
            var options = await OptionsFromJsonAsync("""
                {
                  "Enabled": null,
                  "CaptureRequestBody": null,
                  "CaptureResponseBody": null,
                  "CaptureRequestHeaders": null,
                  "ExcludeDocumentation": null
                }
                """);

            options.Enabled.Should().BeTrue();
            options.CaptureRequestBody.Should().BeTrue();
            options.CaptureResponseBody.Should().BeTrue();
            options.CaptureRequestHeaders.Should().BeTrue();
            options.ExcludeDocumentation.Should().BeTrue();
        }

        [Fact]
        public async Task JsonConfiguration_WithFalseInThePlainOptions_ShouldStayOff()
        {
            var options = await OptionsFromJsonAsync("""
                {
                  "Enabled": false,
                  "CaptureRequestBody": false,
                  "CaptureResponseBody": false,
                  "CaptureRequestHeaders": false,
                  "ExcludeDocumentation": false
                }
                """);

            options.Enabled.Should().BeFalse();
            options.CaptureRequestBody.Should().BeFalse();
            options.CaptureResponseBody.Should().BeFalse();
            options.CaptureRequestHeaders.Should().BeFalse();
            options.ExcludeDocumentation.Should().BeFalse();
        }

        [Fact]
        public async Task JsonConfiguration_WithoutThePlainOptions_ShouldKeepTheirDefaults()
        {
            var options = await OptionsFromJsonAsync("""{ "MaxBodyBytes": 100 }""");

            options.MaxBodyBytes.Should().Be(100);
            options.Enabled.Should().BeTrue();
            options.CaptureRequestBody.Should().BeTrue();
            options.CaptureResponseBody.Should().BeTrue();
            options.CaptureRequestHeaders.Should().BeTrue();
            options.ExcludeDocumentation.Should().BeTrue();
        }

        [Fact]
        public void Capture_ShouldReleaseItsBuffer_AtMostOnce()
        {
            var capture = new BoundedCapture(100);
            capture.Append("hello"u8);

            capture.Release();
            capture.Release();

            capture.GetTextAndRelease().Should().BeNull();
            capture.Total.Should().Be(5, "the size survives");

            var read = new BoundedCapture(100);
            read.Append("hello"u8);

            read.GetTextAndRelease().Should().Be("hello");
            read.Release();
            read.GetTextAndRelease().Should().BeNull();
        }

        private sealed class FixedTime(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        private sealed class HmacSigner : IJwtSigner
        {
            public static readonly byte[] Key = new byte[32];

            public int Calls { get; private set; }

            public string Algorithm => SecurityAlgorithms.HmacSha256;

            public string? KeyId => null;

            public Task<byte[]> SignAsync(byte[] data, CancellationToken cancellationToken = default)
            {
                Calls++;
                return Task.FromResult(HMACSHA256.HashData(Key, data));
            }
        }

        /// <summary>A service written against the contract of 2.10: it must keep compiling and working.</summary>
        private sealed class LegacyTokenService : IJwtTokenService
        {
            public Task<JwtToken> CreateTokenAsync(
                ClaimsIdentity identity,
                IEnumerable<Claim>? additionalClaims = null,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(new JwtToken("legacy", "1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));
        }

        private sealed class InstantTokenService : IJwtTokenService
        {
            public Task<JwtToken> CreateTokenAsync(
                ClaimsIdentity identity,
                IEnumerable<Claim>? additionalClaims = null,
                CancellationToken cancellationToken = default) =>
                throw new NotImplementedException();

            public Task<JwtToken> CreateTokenAsync(
                ClaimsIdentity identity,
                DateTimeOffset expiresAt,
                IEnumerable<Claim>? additionalClaims = null,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(new JwtToken("instant", "1", DateTimeOffset.UtcNow, expiresAt));
        }
    }
}
