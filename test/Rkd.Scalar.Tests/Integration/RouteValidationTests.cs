using Asp.Versioning;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Rkd.Scalar.Infrastructure;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    // [NonController]: these controllers are added only by the tests below (AddExplicitControllers), never by discovery.

    /// <summary>
    /// The mistake that took a real API down: <c>{version}</c> under <c>api/v{version:apiVersion}</c>. With simple
    /// parameters to infer, ASP.NET Core rejects it while building the controllers (inside MapControllers).
    /// </summary>
    [NonController]
    [ApiModule("pacotes")]
    [ApiVersion("1.0")]
    public sealed class RepeatedVersionController : ControllerBase
    {
        [HttpGet("{id}/{version}/{file}")]
        public string Get(string id, string version, string file) => $"{id}/{version}/{file}";
    }

    /// <summary>The same route without inference ([FromRoute]): ASP.NET Core only fails when it builds the route table.</summary>
    [NonController]
    [ApiModule("pacotes")]
    [ApiVersion("1.0")]
    public sealed class RepeatedVersionFromRouteController : ControllerBase
    {
        [HttpGet("{id}/{version}/{file}")]
        public string Get([FromRoute] string id, [FromRoute] string version, [FromRoute] string file) => $"{id}/{version}/{file}";
    }

    [NonController]
    [ApiModule("pacotes")]
    [ApiVersion("1.0")]
    public sealed class RenamedVersionController : ControllerBase
    {
        [HttpGet("{id}/{packageVersion}/{file}")]
        public string Get(string id, string packageVersion, string file) => $"{id}/{packageVersion}/{file}";
    }

    /// <summary>Not an API controller: the repetition between the controller and the action routes only fails on the route table.</summary>
    [NonController]
    [Route("lotes/{id}")]
    public sealed class RepeatedIdController : ControllerBase
    {
        [HttpGet("itens/{id}")]
        public string Get(string id) => id;
    }

    public class RouteValidationTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static Dictionary<string, string?> DocumentationOff => new() { ["RkdScalar:Enabled"] = "false" };

        private static Task<TestApp> StartAsync(
            Type controller,
            Action<RkdScalarBuilder>? configure = null,
            IDictionary<string, string?>? settings = null) =>
            TestApp.StartAsync(
                scalar =>
                {
                    scalar.Services.AddExplicitControllers(controller);
                    scalar.WithVersioning("v1");
                    configure?.Invoke(scalar);
                },
                app => app.MapControllers(),
                settings);

        [Fact]
        public async Task RepeatedVersion_OnAnApiController_ShouldStopTheStartupNamingTheActionAndTheFix()
        {
            var start = () => StartAsync(typeof(RepeatedVersionController));

            var error = (await start.Should().ThrowAsync<InvalidOperationException>()).Which;

            error.Message.Should().Contain($"{typeof(RepeatedVersionController).FullName}.{nameof(RepeatedVersionController.Get)}")
                .And.Contain("'api/v{version:apiVersion}/pacotes/")
                .And.Contain("/{id}/{version}/{file}'")
                .And.Contain("The route parameter name 'version' appears more than one time")
                .And.Contain("give the action's parameter another name, e.g. '{packageVersion}'")
                .And.Contain("WithRouteValidation(false)");
        }

        [Fact]
        public async Task RepeatedVersion_WithoutInference_ShouldStopTheStartupInsteadOfA500OnEveryEndpoint()
        {
            // RkdScalar:Enabled = false: UseRkdScalar maps nothing, so before this check nothing built the routes at startup.
            var start = () => StartAsync(typeof(RepeatedVersionFromRouteController), settings: DocumentationOff);

            var error = (await start.Should().ThrowAsync<InvalidOperationException>()).Which;

            error.Message.Should().Contain(nameof(RepeatedVersionFromRouteController))
                .And.Contain("500 on EVERY endpoint")
                .And.Contain("'{packageVersion}'");
            error.InnerException.Should().BeOfType<RoutePatternException>("the original ASP.NET Core exception stays available");
        }

        [Fact]
        public async Task RepeatedVersion_WithoutInference_AndTheDocumentationOn_ShouldBeExplainedToo()
        {
            var start = () => StartAsync(typeof(RepeatedVersionFromRouteController));

            var error = (await start.Should().ThrowAsync<InvalidOperationException>()).Which;

            error.Message.Should().Contain("'{packageVersion}'");
            error.InnerException.Should().BeOfType<RoutePatternException>();
        }

        [Fact]
        public async Task RepeatedParameterBetweenControllerAndAction_ShouldBeExplained()
        {
            var start = () => StartAsync(typeof(RepeatedIdController), settings: DocumentationOff);

            (await start.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should()
                .Contain("'lotes/{id}/itens/{id}'")
                .And.Contain("appears more than once in the combined route");
        }

        [Fact]
        public async Task WithRouteValidationFalse_OnAnApiController_ShouldFailAsBefore()
        {
            var start = () => StartAsync(typeof(RepeatedVersionController), s => s.WithRouteValidation(false), DocumentationOff);

            // ASP.NET Core's own exception, from MapControllers.
            (await start.Should().ThrowAsync<ArgumentException>()).Which.InnerException.Should().BeOfType<RoutePatternException>();
        }

        [Fact]
        public async Task WithRouteValidationFalse_WithoutInference_ShouldFailWithTheRawException_AsBefore()
        {
            // UseAuthorization builds the route table with the pipeline (AuthorizationPolicyCache): ASP.NET Core's exception,
            // which on IIS (startup errors captured) becomes a 500 on every request.
            var start = () => StartAsync(typeof(RepeatedVersionFromRouteController), s => s.WithRouteValidation(false), DocumentationOff);

            await start.Should().ThrowAsync<RoutePatternException>();
        }

        [Fact]
        public async Task ValidRoutes_ShouldStartAndAnswer()
        {
            await using var app = await StartAsync(typeof(RenamedVersionController));

            var body = await app.Client.GetStringAsync("/api/v1/pacotes/RenamedVersion/a/1.0/b", Ct);

            body.Should().Be("a/1.0/b");
        }

        [Fact]
        public async Task ModelProvider_ShouldRunAfterTheRoutesAreBuiltAndBeforeAspNetCoreParsesThem()
        {
            await using var app = await StartAsync(typeof(RenamedVersionController));

            var orders = app.Services.GetServices<IApplicationModelProvider>().ToDictionary(p => p.GetType().Name, p => p.Order);

            orders[nameof(RouteValidationApplicationModelProvider)].Should()
                .BeGreaterThan(orders["DefaultApplicationModelProvider"])
                .And.BeLessThan(orders["ApiBehaviorApplicationModelProvider"]);
        }

        [Fact]
        public async Task ApplicationWithoutMvc_InDevelopment_ShouldStart()
        {
            // ValidateOnBuild (Development) checks every registration: the model provider must not require MVC services.
            await using var app = await TestApp.StartAsync(
                _ => { },
                app => app.MapGet("/minimal/{id}", (string id) => id),
                environment: "Development");

            (await app.Client.GetStringAsync("/minimal/7", Ct)).Should().Be("7");
        }

        /// <summary>Fails the first build only, like a data source that is not ready yet at startup.</summary>
        private sealed class NotReadyOnceDataSource : EndpointDataSource
        {
            private int _calls;

            public override IReadOnlyList<Endpoint> Endpoints =>
                Interlocked.Increment(ref _calls) == 1 ? throw new InvalidOperationException("Not ready yet.") : [];

            public override IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
        }

        [Fact]
        public async Task OtherEndpointFailures_ShouldNotStopTheStartup()
        {
            // Not a route template: ASP.NET Core keeps handling it as before (it tries again later); only a warning is logged.
            await using var app = await TestApp.StartAsync(
                _ => { },
                app => ((IEndpointRouteBuilder)app).DataSources.Add(new NotReadyOnceDataSource()),
                DocumentationOff);

            app.Should().NotBeNull();
        }

        [Theory]
        [InlineData("api/v{version:apiVersion}/x/{id}/{version}", "'{packageVersion}'")]
        [InlineData("api/v{version:apiVersion}/x/{id}/{**version}", "'{packageVersion}'")]
        [InlineData("a/{id}/b/{ID}", "appears more than once")]
        [InlineData("a/{id:int}/b/{id?}", "appears more than once")]
        public void Hint_ShouldExplainTheRepeatedParameter(string template, string expected) =>
            RouteTemplateValidation.Hint(template).Should().Contain(expected);

        [Theory]
        [InlineData("api/v{version:apiVersion}/x/{id}/{packageVersion}")]
        [InlineData("a/{{id}}/{id}")]
        [InlineData("a/b")]
        public void Hint_ShouldBeNullWithoutRepetition(string template) =>
            RouteTemplateValidation.Hint(template).Should().BeNull();
    }
}
