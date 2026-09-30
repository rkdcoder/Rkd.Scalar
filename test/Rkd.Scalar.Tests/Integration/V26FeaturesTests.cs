using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    [ApiController]
    [Route("v26/systems")]
    public sealed class SystemKeysController : ControllerBase
    {
        [HttpPost("{id:int}/keys")]
        [SensitiveHttpLog]
        public object CreateKey(int id, [FromBody] object request) => new { id, key = "plain-secret-key" };

        [HttpGet("{id:int}")]
        public object Get(int id) => new { id, name = "system" };
    }

    [ApiController]
    [Route("v26/vault")]
    [SensitiveHttpLog]
    public sealed class VaultController : ControllerBase
    {
        [HttpGet]
        public object Get() => new { secret = "vault-secret" };
    }

    public class V26FeaturesTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task SensitiveEndpoints_ShouldNeverStoreBodies()
        {
            var sink = new MemoryHttpLogSink();

            await using var app = await TestApp.StartAsync(
                scalar =>
                {
                    scalar.Services.AddControllers().AddApplicationPart(typeof(SystemKeysController).Assembly);
                    scalar.WithHttpLogSink(_ => sink);
                },
                app =>
                {
                    app.MapControllers();
                    app.MapPost("/v26/tokens/{id}", (int id, object body) => new { token = "plain-token" }).WithSensitiveHttpLog();

                    var group = app.MapGroup("/v26/secure").WithSensitiveHttpLog();
                    group.MapGet("/one", () => new { secret = "group-secret" });
                });

            (await app.Client.PostAsJsonAsync("/v26/systems/7/keys", new { password = "p@ss" }, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await app.Client.PostAsJsonAsync("/v26/tokens/1", new { password = "p@ss" }, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await app.Client.GetStringAsync("/v26/secure/one", Ct)).Should().Contain("group-secret");
            (await app.Client.GetStringAsync("/v26/vault", Ct)).Should().Contain("vault-secret");
            (await app.Client.GetStringAsync("/v26/systems/7", Ct)).Should().Contain("system");

            foreach (var path in new[] { "/v26/systems/7/keys", "/v26/tokens/1", "/v26/secure/one", "/v26/vault" })
            {
                var entry = await sink.WaitForAsync(e => e.Path == path);

                entry.StatusCode.Should().Be(200, path);
                entry.ResponseBody.Should().Be("[REDACTED]", path);
                entry.RequestBody.Should().BeOneOf("[REDACTED]", null);
                entry.RoutePattern.Should().NotBeNull(path);
            }

            (await sink.WaitForAsync(e => e.Path == "/v26/systems/7/keys")).RequestBody.Should().Be("[REDACTED]");
            sink.Entries.Should().NotContain(e =>
                (e.RequestBody ?? "").Contains("p@ss") ||
                (e.ResponseBody ?? "").Contains("plain-secret-key") ||
                (e.ResponseBody ?? "").Contains("plain-token") ||
                (e.ResponseBody ?? "").Contains("secret\":"));

            (await sink.WaitForAsync(e => e.Path == "/v26/systems/7")).ResponseBody.Should().Contain("\"name\":\"system\"",
                "other endpoints of the same controller keep their bodies");
        }

        [Theory]
        [InlineData("Production")]
        [InlineData("Development")]
        public async Task UnexpectedError_ShouldUseTheConfiguredCodeTitleAndDetail(string environment)
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithProblemDetails(o => o
                    .UnexpectedError("ERRO_INESPERADO", "Ocorreu um erro inesperado.", "Erro inesperado")
                    .Map<KeyNotFoundException>(StatusCodes.Status404NotFound)),
                app =>
                {
                    app.MapGet("/v26/boom", string () => throw new InvalidOperationException("db password=secret"));
                    app.MapGet("/v26/missing", string () => throw new KeyNotFoundException("Item 1 not found."));
                },
                environment: environment);

            var response = await app.Client.GetAsync("/v26/boom", Ct);
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            body.GetProperty("code").GetString().Should().Be("ERRO_INESPERADO");
            body.GetProperty("title").GetString().Should().Be("Erro inesperado");
            body.GetProperty("detail").GetString().Should().Be("Ocorreu um erro inesperado.");
            body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();

            if (environment == "Development")
                body.GetProperty("exception").GetProperty("message").GetString().Should().Be("db password=secret");
            else
                body.ToString().Should().NotContain("password");

            // Mapped exceptions are not affected.
            var missingResponse = await app.Client.GetAsync("/v26/missing", Ct);
            missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var missing = JsonDocument.Parse(await missingResponse.Content.ReadAsStringAsync(Ct)).RootElement;
            missing.GetProperty("code").GetString().Should().Be("NOT_FOUND");
        }

        [Fact]
        public async Task UnexpectedError_Default_ShouldStayGeneric()
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithProblemDetails(),
                app => app.MapGet("/v26/boom", string () => throw new InvalidOperationException("boom")),
                environment: "Production");

            var body = JsonDocument.Parse(await (await app.Client.GetAsync("/v26/boom", Ct)).Content.ReadAsStringAsync(Ct)).RootElement;

            body.GetProperty("title").GetString().Should().Be("An unexpected error occurred.");
            body.GetProperty("code").GetString().Should().Be("INTERNAL_SERVER_ERROR");
            body.TryGetProperty("detail", out _).Should().BeFalse();
        }

        [Fact]
        public void UnexpectedError_InvalidCode_ShouldThrow()
        {
            var act = () => new RkdProblemDetailsOptions().UnexpectedError("ERRO INESPERADO");
            act.Should().Throw<ArgumentException>();
        }
    }
}
