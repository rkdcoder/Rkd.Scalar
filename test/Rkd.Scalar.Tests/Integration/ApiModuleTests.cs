using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Asp.Versioning;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    public sealed class ModuleInput
    {
        [Required]
        public string? Name { get; set; }
    }

    [ApiModule("custeio")]
    [ApiVersion("1.0")]
    public sealed class OpcoesController : ControllerBase
    {
        [HttpGet]
        public string Get() => "opcoes";

        [HttpPost]
        public IActionResult Post(ModuleInput input) => Ok(input);
    }

    [ApiModule("custeio")]
    [ApiVersion("1.0")]
    public sealed class ParametrosController : ControllerBase
    {
        [HttpGet]
        public string Get() => "parametros";
    }

    [ApiModule("financeiro", RouteTemplate = "internal/[module]/[controller]", Tag = "Financeiro")]
    public sealed class RelatoriosController : ControllerBase
    {
        [HttpGet]
        public string Get() => "relatorios";
    }

    public class ApiModuleTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static Task<TestApp> StartAsync(Action<RkdScalarBuilder>? configure = null) =>
            TestApp.StartAsync(
                scalar =>
                {
                    scalar.Services.AddControllers().AddApplicationPart(typeof(OpcoesController).Assembly);
                    configure?.Invoke(scalar);
                },
                app => app.MapControllers());

        private static async Task<string?> GetAsync(TestApp app, string url)
        {
            var response = await app.Client.GetAsync(url, Ct);
            return response.StatusCode == HttpStatusCode.OK ? await response.Content.ReadAsStringAsync(Ct) : null;
        }

        private static async Task<Dictionary<string, string[]>> TagsByPathAsync(TestApp app, string document = "v1")
        {
            using var doc = JsonDocument.Parse(await app.Client.GetStringAsync($"/openapi/{document}.json", Ct));

            return doc.RootElement.GetProperty("paths").EnumerateObject().ToDictionary(
                p => p.Name,
                p => p.Value.EnumerateObject().First().Value.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToArray(),
                StringComparer.OrdinalIgnoreCase);   // [controller] keeps the class casing (Opcoes) unless WithLowercaseRouting
        }

        [Fact]
        public async Task WithoutVersioning_ShouldUseApiModuleControllerRoute()
        {
            await using var app = await StartAsync();

            (await GetAsync(app, "/api/custeio/opcoes")).Should().Be("opcoes");
            (await GetAsync(app, "/api/custeio/parametros")).Should().Be("parametros");
        }

        [Fact]
        public async Task WithVersioning_ShouldIncludeTheVersionSegment()
        {
            await using var app = await StartAsync(s => s.WithVersioning("v1"));

            (await GetAsync(app, "/api/v1/custeio/opcoes")).Should().Be("opcoes");
            (await GetAsync(app, "/api/custeio/opcoes")).Should().BeNull();
        }

        [Fact]
        public async Task GlobalTemplate_ShouldApplyToEveryModule()
        {
            await using var app = await StartAsync(s => s.WithApiModules(o => o.RouteTemplate = "[module]/[controller]"));

            (await GetAsync(app, "/custeio/opcoes")).Should().Be("opcoes");
            (await GetAsync(app, "/api/custeio/opcoes")).Should().BeNull();
        }

        [Fact]
        public async Task ControllerTemplate_ShouldWinOverGlobalTemplate()
        {
            await using var app = await StartAsync(s => s.WithApiModules(o => o.RouteTemplate = "[module]/[controller]"));

            (await GetAsync(app, "/internal/financeiro/relatorios")).Should().Be("relatorios");
        }

        [Fact]
        public async Task Module_ShouldBeTheOpenApiTag_AndTagShouldBeCustomizable()
        {
            await using var app = await StartAsync();

            var tags = await TagsByPathAsync(app);

            tags["/api/custeio/opcoes"].Should().Equal("custeio");
            tags["/api/custeio/parametros"].Should().Equal("custeio");
            tags["/internal/financeiro/relatorios"].Should().Equal("Financeiro");
        }

        [Fact]
        public async Task Module_ShouldBehaveAsApiController()
        {
            await using var app = await StartAsync();

            var response = await app.Client.PostAsJsonAsync("/api/custeio/opcoes", new { }, Ct);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "[ApiModule] implies [ApiController] automatic validation");
            (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("Name");
        }

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("a b")]
        [InlineData("{module}")]
        [InlineData("a//b")]
        public void InvalidModuleName_ShouldThrow(string module)
        {
            var act = () => new ApiModuleAttribute(module);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void ModuleName_ShouldBeNormalized_AndTemplateResolved()
        {
            var attribute = new ApiModuleAttribute("/finance/reports/");

            attribute.Module.Should().Be("finance/reports");
            attribute.Template.Should().Be("api/v{version:apiVersion}/finance/reports/[controller]");
            attribute.Tags.Should().Equal("finance/reports");
        }

        [Fact]
        public void TemplateWithoutModuleToken_ShouldThrow()
        {
            var act = () => new ApiModuleOptions { RouteTemplate = "api/[controller]" };

            act.Should().Throw<ArgumentException>().WithMessage("*[module]*");
        }
    }
}
