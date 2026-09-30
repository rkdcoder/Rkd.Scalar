using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Rkd.Scalar.Tests.Helpers;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

namespace Rkd.Scalar.Tests.Integration
{
    public class JwtV2Tests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static readonly Dictionary<string, string?> JwtSettings = new()
        {
            ["Jwt:Secret"] = new string('s', 32),
            ["Jwt:Issuer"] = "issuer",
            ["Jwt:Audience"] = "audience",
            ["Jwt:ExpirationInMinutes"] = "20",
            ["Jwt:ClockSkewInSeconds"] = "0"
        };

        private static Task<TestApp> StartAsync(IDictionary<string, string?>? settings = null) =>
            TestApp.StartAsync(
                scalar => scalar
                    .WithBearerAuth<TestCredentials, FakeCredentialValidator>()   // "Jwt" section by default
                    .WithJwtLoginEndpoint<TestCredentials>(),                      // "/auth/login", 5 per minute
                app => app.MapGet("/me", (ClaimsPrincipal user) => new
                    {
                        name = user.Identity!.Name,
                        id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                        reader = user.IsInRole("reader"),
                        writer = user.IsInRole("writer"),
                        tenant = user.FindFirst("tenant")?.Value
                    })
                    .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = RkdScalarAuthenticationSchemes.Bearer }),
                settings: settings ?? JwtSettings);

        [Fact]
        public async Task StandardClaims_ShouldRoundTripToClaimTypes()
        {
            await using var app = await StartAsync();

            var login = await app.Client.PostAsJsonAsync("/auth/login", new { username = "user", password = "pass" }, Ct);
            login.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await login.Content.ReadFromJsonAsync<JsonElement>(Ct);
            body.GetProperty("expires_in").GetInt64().Should().BeInRange(1190, 1200);

            var token = body.GetProperty("access_token").GetString()!;
            token.Should().NotContain("schemas.xmlsoap.org");

            var request = new HttpRequestMessage(HttpMethod.Get, "/me");
            request.Headers.Authorization = new("Bearer", token);
            var me = await (await app.Client.SendAsync(request, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

            me.GetProperty("name").GetString().Should().Be("user");
            me.GetProperty("id").GetString().Should().Be("42");
            me.GetProperty("reader").GetBoolean().Should().BeTrue();
            me.GetProperty("writer").GetBoolean().Should().BeTrue();
            me.GetProperty("tenant").GetString().Should().Be("acme");
        }

        [Fact]
        public async Task LoginFailure_ShouldReturnProblemDetailsWithReason()
        {
            await using var app = await StartAsync();

            var locked = await app.Client.PostAsJsonAsync("/auth/login", new { username = "locked", password = "x" }, Ct);
            locked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            locked.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            (await locked.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("detail").GetString().Should().Be("Account locked.");

            var wrong = await app.Client.PostAsJsonAsync("/auth/login", new { username = "user", password = "wrong" }, Ct);
            wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Theory]
        [InlineData("Jwt:Audience", "*'Audience' is required*")]
        [InlineData("Jwt:Issuer", "*'Issuer' is required*")]
        public async Task MissingRequiredSetting_ShouldFailAtStartup(string removedKey, string message)
        {
            var settings = new Dictionary<string, string?>(JwtSettings) { [removedKey] = "" };

            var act = () => StartAsync(settings);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
        }

        [Fact]
        public async Task InvalidExpiration_ShouldFailAtStartup()
        {
            var settings = new Dictionary<string, string?>(JwtSettings) { ["Jwt:ExpirationInMinutes"] = "0" };

            var act = () => StartAsync(settings);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'ExpirationInMinutes' must be greater than zero*");
        }
    }
}
