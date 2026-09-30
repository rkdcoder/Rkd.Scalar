using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    public sealed class MemoryHttpLogSink : IHttpLogSink
    {
        public ConcurrentQueue<HttpLogEntry> Entries { get; } = new();

        public ConcurrentQueue<int> BatchSizes { get; } = new();

        public Task WriteAsync(IReadOnlyList<HttpLogEntry> entries, CancellationToken cancellationToken)
        {
            BatchSizes.Enqueue(entries.Count);

            foreach (var entry in entries)
                Entries.Enqueue(entry);

            return Task.CompletedTask;
        }

        public async Task<HttpLogEntry> WaitForAsync(Func<HttpLogEntry, bool> match)
        {
            for (var i = 0; i < 200; i++)
            {
                var found = Entries.FirstOrDefault(match);

                if (found is not null)
                    return found;

                await Task.Delay(25);
            }

            throw new TimeoutException("The HTTP log entry was not written.");
        }
    }

    public sealed class FailingHttpLogSink : IHttpLogSink
    {
        public int Calls;

        public Task WriteAsync(IReadOnlyList<HttpLogEntry> entries, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("database down");
        }
    }

    public class HttpLoggingTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static async Task<(TestApp App, MemoryHttpLogSink Sink)> StartAsync(
            Action<RkdScalarBuilder>? configure = null,
            Action<WebApplication>? endpoints = null,
            IDictionary<string, string?>? settings = null)
        {
            var sink = new MemoryHttpLogSink();

            var app = await TestApp.StartAsync(
                scalar =>
                {
                    scalar.WithHttpLogSink(_ => sink);
                    configure?.Invoke(scalar);
                },
                app =>
                {
                    app.MapGet("/items/{id:int}", (int id) => new { id, name = "chair" });
                    app.MapPost("/items", (Item item) => Results.Created($"/items/{item.Id}", item));
                    app.MapGet("/health", () => "ok");
                    app.MapGet("/boom", string () => throw new InvalidOperationException("kaboom"));
                    app.MapGet("/conflict", string () => throw RkdError.Conflict("ITEM_EXISTS", "Already exists."));
                    app.MapGet("/binary", () => Results.Bytes(new byte[5000], "application/octet-stream"));
                    app.MapGet("/big", () => Results.Text(new string('x', 10_000), "text/plain"));
                    app.MapGet("/me", (ClaimsPrincipal user) => "me").RequireAuthorization(
                        new AuthorizeAttribute { AuthenticationSchemes = RkdScalarAuthenticationSchemes.Bearer });
                    endpoints?.Invoke(app);
                },
                settings: settings);

            return (app, sink);
        }

        public sealed record Item(int Id, string Name, string? Password = null);

        [Fact]
        public async Task Request_ShouldBeLoggedWithRedactedCredentials()
        {
            var (app, sink) = await StartAsync(scalar => scalar.WithApiKeyAuth("ApiKeys", o => o.HeaderName = "X-Partner-Key"),
                settings: new Dictionary<string, string?> { ["ApiKeys:partner"] = "key-1" });
            await using var _ = app;

            var request = new HttpRequestMessage(HttpMethod.Get, "/items/5?page=2&access_token=secret-token");
            request.Headers.Authorization = new("Bearer", "abc.def.ghi");
            request.Headers.Add("X-Partner-Key", "key-1");
            request.Headers.Add("Cookie", "session=very-secret");
            request.Headers.UserAgent.ParseAdd("tests/1.0");
            (await app.Client.SendAsync(request, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

            var entry = await sink.WaitForAsync(e => e.Path == "/items/5");

            entry.Method.Should().Be("GET");
            entry.StatusCode.Should().Be(200);
            entry.IsSuccess.Should().BeTrue();
            entry.RoutePattern.Should().Be("/items/{id:int}");
            entry.QueryString.Should().Be("?page=2&access_token=[REDACTED]");
            entry.RequestHeaders["Authorization"].Should().Be("Bearer [REDACTED]");
            entry.RequestHeaders["X-Partner-Key"].Should().Be("[REDACTED]");
            entry.RequestHeaders["Cookie"].Should().Be("[REDACTED]");
            entry.UserAgent.Should().Be("tests/1.0");
            entry.ResponseBody.Should().Be("""{"id":5,"name":"chair"}""");
            entry.ResponseContentType.Should().StartWith("application/json");
            entry.ResponseSize.Should().Be(23);
            entry.TraceId.Should().NotBeNullOrEmpty();
            entry.Duration.Should().BePositive();
            entry.StartedAt.Offset.Should().Be(TimeSpan.Zero);
            entry.ApplicationName.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task Bodies_ShouldBeCapturedTruncatedOrSkipped()
        {
            var (app, sink) = await StartAsync(scalar => scalar.WithHttpLogging(o => o.MaxBodyBytes = 1000));
            await using var _ = app;

            var created = await app.Client.PostAsJsonAsync("/items", new Item(7, "table"), Ct);
            created.StatusCode.Should().Be(HttpStatusCode.Created);

            var post = await sink.WaitForAsync(e => e.Method == "POST");
            post.RequestBody.Should().Contain("\"name\":\"table\"");
            post.RequestContentType.Should().StartWith("application/json");
            post.ResponseBody.Should().Contain("\"id\":7");

            // Larger than MaxBodyBytes: the client gets everything, the log keeps the first 1000 bytes.
            (await app.Client.GetStringAsync("/big", Ct)).Should().HaveLength(10_000);
            var big = await sink.WaitForAsync(e => e.Path == "/big");
            big.ResponseBody.Should().HaveLength(1000);
            big.ResponseBodyTruncated.Should().BeTrue();
            big.ResponseSize.Should().Be(10_000);

            // Binary: not captured, size recorded.
            (await app.Client.GetByteArrayAsync("/binary", Ct)).Should().HaveCount(5000);
            var binary = await sink.WaitForAsync(e => e.Path == "/binary");
            binary.ResponseBody.Should().BeNull();
            binary.ResponseSize.Should().Be(5000);
        }

        [Fact]
        public async Task SensitivePathsAndLogin_ShouldNeverStoreBodies()
        {
            var (app, sink) = await StartAsync(
                scalar => scalar
                    .WithHttpLogging(o => o.SensitivePaths.Add("/items"))
                    .WithBearerAuth<TestCredentials, FakeCredentialValidator>(TestJwt.Options(o => o.Secret = new string('s', 32)))
                    .WithJwtLoginEndpoint<TestCredentials>());
            await using var _ = app;

            await app.Client.PostAsJsonAsync("/items", new Item(1, "x", Password: "p@ss"), Ct);
            var login = await app.Client.PostAsJsonAsync("/auth/login", new { username = "user", password = "pass" }, Ct);
            login.StatusCode.Should().Be(HttpStatusCode.OK);

            var items = await sink.WaitForAsync(e => e.Path == "/items");
            items.RequestBody.Should().Be("[REDACTED]");
            items.ResponseBody.Should().Be("[REDACTED]");

            var auth = await sink.WaitForAsync(e => e.Path == "/auth/login");
            auth.RequestBody.Should().Be("[REDACTED]");
            auth.ResponseBody.Should().Be("[REDACTED]", "the response carries the access token");
        }

        [Fact]
        public async Task User_ShouldComeFromConfigurableClaims()
        {
            var options = TestJwt.Options(o => o.Secret = new string('s', 32));

            var (app, sink) = await StartAsync(scalar => scalar
                .WithBearerAuth(options)
                .WithHttpLogging(o =>
                {
                    o.UserNameClaimTypes = ["cpf"];
                    o.UserIdClaimTypes = ["employee_id"];
                }));
            await using var _ = app;

            var token = await TestJwt.Service(options).CreateTokenAsync(
                new ClaimsIdentity([new Claim(ClaimTypes.Name, "rodrigo"), new Claim("cpf", "123.456.789-00"), new Claim("employee_id", "E42")], "test"),
                cancellationToken: Ct);

            var request = new HttpRequestMessage(HttpMethod.Get, "/me");
            request.Headers.Authorization = new("Bearer", token.AccessToken);
            (await app.Client.SendAsync(request, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

            var entry = await sink.WaitForAsync(e => e.Path == "/me");
            entry.UserName.Should().Be("123.456.789-00");
            entry.UserId.Should().Be("E42");
        }

        [Fact]
        public async Task Errors_ShouldCarryCodeAndException()
        {
            var (app, sink) = await StartAsync(scalar => scalar.WithProblemDetails());
            await using var _ = app;

            (await app.Client.GetAsync("/boom", Ct)).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            (await app.Client.GetAsync("/conflict", Ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await app.Client.GetAsync("/nowhere", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

            var boom = await sink.WaitForAsync(e => e.Path == "/boom");
            boom.StatusCode.Should().Be(500);
            boom.IsSuccess.Should().BeFalse();
            boom.ErrorCode.Should().Be("INTERNAL_SERVER_ERROR");
            boom.Exception.Should().Contain("kaboom").And.Contain("InvalidOperationException");
            boom.ResponseBody.Should().Contain("\"traceId\"");
            boom.ResponseBody.Should().Contain(boom.TraceId, "the traceId of the response is the one of the log entry");

            var conflict = await sink.WaitForAsync(e => e.Path == "/conflict");
            conflict.ErrorCode.Should().Be("ITEM_EXISTS");
            conflict.Exception.Should().BeNull("expected business errors are not exceptions to investigate");

            (await sink.WaitForAsync(e => e.Path == "/nowhere")).ErrorCode.Should().Be("NOT_FOUND");
        }

        [Fact]
        public async Task UnhandledException_WithoutProblemDetails_ShouldBeLoggedAs500()
        {
            var (app, sink) = await StartAsync();
            await using var _ = app;

            try
            {
                await app.Client.GetAsync("/boom", Ct);
            }
            catch (Exception)
            {
                // TestServer surfaces unhandled exceptions to the client.
            }

            var entry = await sink.WaitForAsync(e => e.Path == "/boom");
            entry.StatusCode.Should().Be(500);
            entry.Exception.Should().Contain("kaboom");
        }

        [Fact]
        public async Task ExclusionsFilterAndEnrich()
        {
            var (app, sink) = await StartAsync(scalar => scalar.WithHttpLogging(o =>
            {
                o.ExcludedPaths.Add("/health");
                o.Filter = context => context.Request.Path != "/items/2";
                o.Enrich = (context, entry) => entry.Properties["tenant"] = context.Request.Headers["X-Tenant"].ToString();
            }));
            await using var _ = app;

            await app.Client.GetAsync("/health", Ct);
            await app.Client.GetAsync("/openapi/v1.json", Ct);
            await app.Client.GetAsync("/scalar/", Ct);
            await app.Client.GetAsync("/items/2", Ct);

            var request = new HttpRequestMessage(HttpMethod.Get, "/items/3");
            request.Headers.Add("X-Tenant", "acme");
            await app.Client.SendAsync(request, Ct);

            var entry = await sink.WaitForAsync(e => e.Path == "/items/3");
            entry.Properties["tenant"].Should().Be("acme");

            sink.Entries.Select(e => e.Path).Should().BeEquivalentTo(["/items/3"]);
        }

        [Fact]
        public async Task ConfigurationSection_ShouldBind()
        {
            var (app, sink) = await StartAsync(settings: new Dictionary<string, string?>
            {
                ["HttpLogging:CaptureResponseBody"] = "false",
                ["HttpLogging:ExcludedPaths:0"] = "/health",
                ["HttpLogging:UserNameClaimTypes:0"] = "email"
            });
            await using var _ = app;

            await app.Client.GetAsync("/health", Ct);
            await app.Client.GetAsync("/items/9", Ct);

            var entry = await sink.WaitForAsync(e => e.Path == "/items/9");
            entry.ResponseBody.Should().BeNull();
            sink.Entries.Should().NotContain(e => e.Path == "/health");

            app.Services.GetRequiredService<RkdHttpLoggingOptions>().UserNameClaimTypes.Should().Equal("email");
        }

        [Fact]
        public async Task FailingSink_ShouldNeverAffectRequests_NorOtherSinks()
        {
            var failing = new FailingHttpLogSink();
            var (app, sink) = await StartAsync(scalar => scalar.WithHttpLogSink(_ => failing));
            await using var _ = app;

            for (var i = 0; i < 5; i++)
                (await app.Client.GetAsync($"/items/{i}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

            await sink.WaitForAsync(e => e.Path == "/items/4");
            failing.Calls.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task Burst_ShouldBeWrittenInBatches()
        {
            var (app, sink) = await StartAsync(scalar => scalar.WithHttpLogging(o => o.BatchSize = 50));
            await using var _ = app;

            await Task.WhenAll(Enumerable.Range(0, 200).Select(i => app.Client.GetAsync($"/items/{i}", Ct)));

            await sink.WaitForAsync(e => e.Path == "/items/199");
            for (var i = 0; i < 100 && sink.Entries.Count < 200; i++)
                await Task.Delay(25, Ct);

            sink.Entries.Should().HaveCount(200);
            sink.BatchSizes.Should().OnlyContain(size => size <= 50);
        }

        [Fact]
        public async Task QueuedEntries_ShouldBeWrittenOnShutdown()
        {
            var (app, sink) = await StartAsync();

            await app.Client.GetAsync("/items/1", Ct);
            await app.DisposeAsync();

            sink.Entries.Should().Contain(e => e.Path == "/items/1");
        }

        [Fact]
        public void WithoutSink_ShouldFailAtStartup()
        {
            var act = () => TestApp.StartAsync(scalar => scalar.WithHttpLogging()).GetAwaiter().GetResult();

            act.Should().Throw<InvalidOperationException>().WithMessage("*no destination*");
        }

        [Fact]
        public async Task Disabled_ShouldNotLog()
        {
            var (app, sink) = await StartAsync(scalar => scalar.WithHttpLogging(o => o.Enabled = false));
            await using var _ = app;

            await app.Client.GetAsync("/items/1", Ct);
            await Task.Delay(200, Ct);

            sink.Entries.Should().BeEmpty();
        }

        [Fact]
        public async Task StreamedResponse_ShouldReachTheClientUnchanged()
        {
            var (app, sink) = await StartAsync(endpoints: app => app.MapGet("/stream", async (HttpContext context) =>
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
            entry.ResponseBody.Should().Be("data: 0\n\ndata: 1\n\ndata: 2\n\n");
        }
    }
}
