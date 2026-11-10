using System.Text.Json;
using Asp.Versioning;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    // [NonController]: added only by the tests below (AddExplicitControllers). Without WithVersioning the [ApiModule] route
    // is api/[module]/[controller], with no {version} segment.

    [NonController]
    [ApiModule("modulos")]
    public sealed class VersionInTheRouteController : ControllerBase
    {
        [HttpGet("{version}")]
        public string Get(string version) => version;
    }

    [NonController]
    [ApiModule("modulos")]
    public sealed class VersionInTheQueryController : ControllerBase
    {
        [HttpGet]
        public string Get(string? version) => version ?? "(none)";
    }

    [NonController]
    [ApiModule("modulos")]
    [ApiVersion("1.0")]
    public sealed class VersionedQueryController : ControllerBase
    {
        [HttpGet]
        public string Get(string? version) => version ?? "(none)";
    }

    [NonController]
    [ApiModule("modulos")]
    public sealed class TenantController : ControllerBase
    {
        [HttpGet]
        public string Get(string? tenant) => tenant ?? "(none)";
    }

    /// <summary>
    /// On API controllers ASP.NET Core infers where each simple parameter comes from (route or query string) by reading the
    /// routes while it builds the controllers: the effective [ApiModule] route must already be there at that moment.
    /// </summary>
    public class ApiModuleInferenceTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static Task<TestApp> StartAsync(Type controller, Action<RkdScalarBuilder>? configure = null) =>
            TestApp.StartAsync(
                scalar =>
                {
                    scalar.Services.AddExplicitControllers(controller);
                    configure?.Invoke(scalar);
                },
                app => app.MapControllers());

        [Fact]
        public async Task WithoutVersioning_AVersionRouteParameter_ShouldWork()
        {
            await using var app = await StartAsync(typeof(VersionInTheRouteController));

            (await app.Client.GetStringAsync("/api/modulos/VersionInTheRoute/1.2", Ct)).Should().Be("1.2");
        }

        [Fact]
        public async Task WithoutVersioning_AVersionParameter_ShouldBindFromTheQueryString()
        {
            await using var app = await StartAsync(typeof(VersionInTheQueryController));

            (await app.Client.GetStringAsync("/api/modulos/VersionInTheQuery?version=3", Ct)).Should().Be("3");
        }

        [Fact]
        public async Task WithoutVersioning_AVersionParameter_ShouldBeDocumentedInTheQueryString()
        {
            await using var app = await StartAsync(typeof(VersionInTheQueryController));

            using var document = JsonDocument.Parse(await app.Client.GetStringAsync("/openapi/v1.json", Ct));

            var parameter = document.RootElement.GetProperty("paths").EnumerateObject()
                .Single(path => path.Name.Contains("VersionInTheQuery", StringComparison.OrdinalIgnoreCase))
                .Value.GetProperty("get").GetProperty("parameters").EnumerateArray()
                .Single(p => p.GetProperty("name").GetString() == "version");

            parameter.GetProperty("in").GetString().Should().Be("query");
        }

        [Fact]
        public async Task GlobalTemplateParameter_ShouldBindFromTheRoute()
        {
            await using var app = await StartAsync(
                typeof(TenantController),
                s => s.WithApiModules(o => o.RouteTemplate = "api/{tenant}/[module]/[controller]"));

            (await app.Client.GetStringAsync("/api/acme/modulos/Tenant", Ct)).Should().Be("acme");
        }

        [Fact]
        public async Task WithVersioning_TheVersionParameter_StillComesFromTheRoute()
        {
            // Unchanged: api/v{version:apiVersion}/... has {version}, so the parameter is the version segment.
            await using var app = await StartAsync(typeof(VersionedQueryController), s => s.WithVersioning("v1"));

            (await app.Client.GetStringAsync("/api/v1/modulos/VersionedQuery?version=3", Ct)).Should().Be("1");
        }
    }
}
