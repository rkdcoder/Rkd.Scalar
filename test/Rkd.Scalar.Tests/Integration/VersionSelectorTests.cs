using Asp.Versioning;
using Asp.Versioning.Builder;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Rkd.Scalar.Configuration;
using Rkd.Scalar.Extensions;
using Rkd.Scalar.Tests.Helpers;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rkd.Scalar.Tests.Integration
{
    public partial class VersionSelectorTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static Task<TestApp> StartAsync(bool versionSelector = true, bool singleVersion = false) =>
            TestApp.StartAsync(
                scalar => scalar.WithVersioning(singleVersion ? ["v1"] : ["v1", "v2"]),
                app =>
                {
                    var builder = app.NewApiVersionSet();
                    builder = singleVersion
                        ? builder.HasApiVersion(new ApiVersion(1, 0))
                        : builder.HasDeprecatedApiVersion(new ApiVersion(1, 0)).HasApiVersion(new ApiVersion(2, 0));

                    app.MapGet("/api/v{version:apiVersion}/ping", () => "pong").WithApiVersionSet(builder.Build());
                },
                useScalar: app => app.UseRkdScalar(new RkdScalarConfiguration { VersionSelector = versionSelector }));

        private static async Task<JsonElement[]> GetSourcesAsync(TestApp app, string url)
        {
            var response = await app.Client.GetAsync(url, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var html = await response.Content.ReadAsStringAsync(Ct);
            var match = SourcesRegex().Match(html);
            match.Success.Should().BeTrue("the Scalar page must declare its sources");

            return JsonDocument.Parse(match.Groups[1].Value).RootElement.EnumerateArray().ToArray();
        }

        private static bool IsDefault(JsonElement source) =>
            source.TryGetProperty("default", out var value) && value.GetBoolean();

        [Fact]
        public async Task Root_ShouldListEveryVersion_NewestFirst_AndSelectNewest()
        {
            await using var app = await StartAsync();

            var sources = await GetSourcesAsync(app, "/scalar/");

            sources.Select(s => s.GetProperty("title").GetString())
                .Should().Equal("v2", "v1 (deprecated)");
            IsDefault(sources[0]).Should().BeTrue();
            IsDefault(sources[1]).Should().BeFalse();
        }

        [Fact]
        public async Task VersionUrl_ShouldRedirectToSelectorWithThatVersion()
        {
            await using var app = await StartAsync();

            var response = await app.Client.GetAsync("/scalar/v1", Ct);

            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            response.Headers.Location!.ToString().Should().Be("/scalar/?document=v1");

            var sources = await GetSourcesAsync(app, "/scalar/?document=v1");
            sources.Should().HaveCount(2);
            IsDefault(sources.Single(s => s.GetProperty("url").GetString()!.Contains("v1"))).Should().BeTrue();
        }

        [Fact]
        public async Task UnknownSegment_ShouldNotRedirect()
        {
            await using var app = await StartAsync();

            (await app.Client.GetAsync("/scalar/v9", Ct)).StatusCode.Should().NotBe(HttpStatusCode.Redirect);
            (await app.Client.GetAsync("/scalar/scalar.js", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task SingleVersion_ShouldKeepVersionUrl()
        {
            await using var app = await StartAsync(singleVersion: true);

            var sources = await GetSourcesAsync(app, "/scalar/v1");

            sources.Should().ContainSingle();
        }

        [Fact]
        public async Task VersionSelectorDisabled_ShouldKeepOnePagePerVersion()
        {
            await using var app = await StartAsync(versionSelector: false);

            var sources = await GetSourcesAsync(app, "/scalar/v1");

            sources.Should().ContainSingle().Which.GetProperty("title").GetString().Should().Be("v1");
        }

        [GeneratedRegex("\"sources\":(\\[[^\\]]*\\])")]
        private static partial Regex SourcesRegex();
    }
}
