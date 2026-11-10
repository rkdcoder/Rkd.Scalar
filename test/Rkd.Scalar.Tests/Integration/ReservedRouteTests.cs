using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rkd.Scalar.Infrastructure;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    [NonController]
    [Route("openapi/extra")]
    public sealed class UnderOpenApiController : ControllerBase
    {
        [HttpGet]
        public string Get() => "extra";
    }

    /// <summary>
    /// Endpoints of the application under the paths of the documentation (<c>/scalar</c>, <c>/openapi</c>): reported at
    /// startup, without stopping the application, since they worked until now.
    /// </summary>
    public class ReservedRouteTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static Task<TestApp> StartAsync(
            CollectingLoggerProvider logs,
            Action<WebApplication> map,
            IDictionary<string, string?>? settings = null,
            Action<RkdScalarBuilder>? configure = null) =>
            TestApp.StartAsync(
                scalar => configure?.Invoke(scalar),
                map,
                settings,
                configureBuilder: builder => builder.Logging.AddProvider(logs));

        private static List<string> ReservedRouteWarnings(CollectingLoggerProvider logs) =>
            logs.Entries
                .Where(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("where Rkd.Scalar serves the API documentation"))
                .Select(entry => entry.Message)
                .ToList();

        [Fact]
        public async Task EndpointUnderTheScalarPath_ShouldBeReported_AndKeepWorking()
        {
            var logs = new CollectingLoggerProvider();

            await using var app = await StartAsync(logs, a => a.MapGet("/scalar/admin", () => "admin"));

            ReservedRouteWarnings(logs).Should().ContainSingle()
                .Which.Should().Contain("/scalar/admin").And.Contain("'/scalar'");
            (await app.Client.GetStringAsync("/scalar/admin", Ct)).Should().Be("admin");
        }

        [Fact]
        public async Task ControllerUnderTheOpenApiPath_ShouldBeReported()
        {
            var logs = new CollectingLoggerProvider();

            await using var app = await StartAsync(
                logs,
                a => a.MapControllers(),
                configure: s => s.Services.AddExplicitControllers(typeof(UnderOpenApiController)));

            ReservedRouteWarnings(logs).Should().ContainSingle()
                .Which.Should().Contain(nameof(UnderOpenApiController)).And.Contain("/openapi/extra").And.Contain("'/openapi'");
        }

        [Fact]
        public async Task TheDocumentationItself_AndSimilarPaths_ShouldNotBeReported()
        {
            var logs = new CollectingLoggerProvider();

            await using var app = await StartAsync(logs, a =>
            {
                a.MapGet("/scalars/list", () => "list");
                a.MapGet("/api/scalar", () => "api");
                a.MapGet("/openapi-proxy", () => "proxy");
            });

            ReservedRouteWarnings(logs).Should().BeEmpty();
            (await app.Client.GetAsync("/openapi/v1.json", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task TheDocumentationEndpoints_ShouldBeUnderTheReservedPaths_AndMarked()
        {
            // What keeps them out of the warnings is the mark, not their paths.
            await using var app = await StartAsync(new CollectingLoggerProvider(), _ => { });

            var documentation = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
                .Where(e => e.RoutePattern.RawText?.TrimStart('/').StartsWith("scalar", StringComparison.OrdinalIgnoreCase) == true
                    || e.RoutePattern.RawText?.TrimStart('/').StartsWith("openapi/", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            documentation.Should().NotBeEmpty();
            documentation.Should().OnlyContain(e => e.Metadata.GetMetadata<ReservedRouteGuard.DocumentationEndpoint>() != null);
        }

        [Theory]
        [InlineData("/openapi/{documentName}.json", "/openapi")]
        [InlineData("docs/api/{documentName}.json", "/docs/api")]
        [InlineData("/scalar", "/scalar")]
        [InlineData("/scalar/", "/scalar")]
        [InlineData("/{documentName}.json", null)]
        [InlineData("", null)]
        public void FixedStart_ShouldBeTheLiteralSegmentsBeforeTheFirstParameter(string pattern, string? expected) =>
            ReservedRouteGuard.FixedStart(pattern).Should().Be(expected);

        [Fact]
        public async Task WithTheDocumentationOff_NothingIsReserved()
        {
            var logs = new CollectingLoggerProvider();

            await using var app = await StartAsync(
                logs,
                a => a.MapGet("/scalar/admin", () => "admin"),
                new Dictionary<string, string?> { ["RkdScalar:Enabled"] = "false" });

            ReservedRouteWarnings(logs).Should().BeEmpty();
        }

        [Fact]
        public async Task CustomPaths_ShouldBeTheReservedOnes()
        {
            var logs = new CollectingLoggerProvider();

            await using var app = await StartAsync(
                logs,
                a =>
                {
                    a.MapGet("/docs/admin", () => "admin");
                    a.MapGet("/specs/extra", () => "extra");
                    a.MapGet("/scalar/admin", () => "free");
                    a.MapGet("/openapi/extra", () => "free");
                },
                new Dictionary<string, string?>
                {
                    ["RkdScalar:ScalarRoutePrefix"] = "/docs",
                    ["RkdScalar:OpenApiRoutePattern"] = "/specs/{documentName}.json"
                });

            ReservedRouteWarnings(logs).Should().HaveCount(2)
                .And.Contain(m => m.Contains("/docs/admin"))
                .And.Contain(m => m.Contains("/specs/extra"));
        }
    }
}
