using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    /// <summary>
    /// Applications that protect every route with a FallbackPolicy: the documentation is governed by
    /// <c>RkdScalar:Enabled</c> and <c>WithUiProtection</c>, never by the API authorization.
    /// </summary>
    public class FallbackPolicyTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static Task<TestApp> StartAsync(
            Action<RkdScalarBuilder>? configure = null,
            IDictionary<string, string?>? settings = null,
            bool versionSelector = true) =>
            TestApp.StartAsync(
                scalar =>
                {
                    scalar.Services.AddAuthorization(o => o.FallbackPolicy =
                        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

                    scalar.WithBearerAuth(TestJwt.Options(o => o.Secret = new string('s', 32)))
                        .WithProblemDetails();

                    configure?.Invoke(scalar);
                },
                app => app.MapGet("/api/data", () => "secret"),
                settings: settings,
                useScalar: app => app.UseRkdScalar(o => o.VersionSelector = versionSelector));

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Documentation_ShouldBeAvailable(bool versionSelector)
        {
            await using var app = await StartAsync(versionSelector: versionSelector);

            (await app.Client.GetAsync("/openapi/v1.json", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await app.Client.GetAsync(versionSelector ? "/scalar/" : "/scalar/v1", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

            if (versionSelector)
            {
                var redirect = await app.Client.GetAsync("/scalar/v1", Ct);
                ((int)redirect.StatusCode).Should().BeOneOf(200, 301, 302, 307, 308);
            }

            // The API itself stays protected.
            (await app.Client.GetAsync("/api/data", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task UiProtection_ShouldStillDecide()
        {
            await using var app = await StartAsync(scalar => scalar.WithUiProtection("admin", "docs-pass"));

            foreach (var url in new[] { "/scalar/", "/openapi/v1.json" })
            {
                var anonymous = await app.Client.GetAsync(url, Ct);
                anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);
                anonymous.Headers.WwwAuthenticate.Should().Contain(h => h.Scheme == "Basic", url);

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:docs-pass")));
                (await app.Client.SendAsync(request, Ct)).StatusCode.Should().Be(HttpStatusCode.OK, url);
            }
        }

        [Fact]
        public async Task DisabledDocumentation_ShouldNotBeServed()
        {
            await using var app = await StartAsync(settings: new Dictionary<string, string?> { ["RkdScalar:Enabled"] = "false" });

            // No documentation endpoint exists: with a FallbackPolicy, ASP.NET Core answers unmapped routes with 401
            // (the policy also covers requests without an endpoint), never with the documentation.
            foreach (var url in new[] { "/scalar/", "/openapi/v1.json" })
            {
                var response = await app.Client.GetAsync(url, Ct);
                response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);
                (await response.Content.ReadAsStringAsync(Ct)).Should().NotContain("\"openapi\":").And.NotContain("<html", url);
            }
        }

        [Fact]
        public async Task DisabledDocumentation_WithoutFallbackPolicy_ShouldBe404()
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithProblemDetails(),
                settings: new Dictionary<string, string?> { ["RkdScalar:Enabled"] = "false" });

            (await app.Client.GetAsync("/scalar/", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await app.Client.GetAsync("/openapi/v1.json", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}
