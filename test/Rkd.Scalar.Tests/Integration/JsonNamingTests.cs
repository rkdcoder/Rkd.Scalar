using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    public enum ContractStatus { Active, OnHold }

    public sealed class ContractDto
    {
        [Range(1, 1000)]
        public decimal UnitPrice { get; set; }

        public DateOnly SignedAt { get; set; }

        public ContractStatus CurrentStatus { get; set; }

        public Dictionary<string, int> ExtraFees { get; set; } = new();
    }

    [ApiController]
    [Route("json-naming/contracts")]
    public sealed class JsonNamingController : ControllerBase
    {
        [HttpGet]
        public ContractDto Get() => JsonNamingTests.Sample;

        [HttpPost]
        public ContractDto Post(ContractDto contract) => contract;
    }

    public class JsonNamingTests
    {
        internal static readonly ContractDto Sample = new()
        {
            UnitPrice = 12.5m,
            SignedAt = new DateOnly(2026, 9, 30),
            CurrentStatus = ContractStatus.OnHold,
            ExtraFees = { ["LateFee"] = 3 }
        };

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static Task<TestApp> StartAsync(Action<RkdScalarBuilder>? configure = null) =>
            TestApp.StartAsync(
                scalar =>
                {
                    scalar.Services.AddControllers().AddApplicationPart(typeof(JsonNamingController).Assembly);
                    configure?.Invoke(scalar);
                },
                app =>
                {
                    app.MapControllers();
                    app.MapGet("/json-naming/minimal", () => Sample);
                });

        private static async Task<JsonElement> GetJsonAsync(TestApp app, string url) =>
            JsonDocument.Parse(await app.Client.GetStringAsync(url, Ct)).RootElement;

        private static IEnumerable<string> Names(JsonElement element) =>
            element.EnumerateObject().Select(p => p.Name);

        [Fact]
        public async Task WithJsonNaming_ShouldApplyToControllersAndMinimalApis()
        {
            await using var app = await StartAsync(s => s.WithJsonNaming(JsonNamingPolicy.SnakeCaseLower));

            foreach (var url in new[] { "/json-naming/contracts", "/json-naming/minimal" })
            {
                var body = await GetJsonAsync(app, url);

                Names(body).Should().BeEquivalentTo("unit_price", "signed_at", "current_status", "extra_fees");
                Names(body.GetProperty("extra_fees")).Should().Equal("late_fee");
            }
        }

        [Fact]
        public async Task WithJsonNaming_ShouldApplyToTheOpenApiSchemasShownByScalar()
        {
            await using var app = await StartAsync(s => s.WithJsonNaming(JsonNamingPolicy.SnakeCaseLower));

            var schema = (await GetJsonAsync(app, "/openapi/v1.json"))
                .GetProperty("components").GetProperty("schemas").GetProperty(nameof(ContractDto));

            Names(schema.GetProperty("properties")).Should().BeEquivalentTo("unit_price", "signed_at", "current_status", "extra_fees");
        }

        [Fact]
        public async Task WithJsonNaming_ShouldReadRequestsAndNameValidationErrors()
        {
            await using var app = await StartAsync(s => s.WithJsonNaming(JsonNamingPolicy.SnakeCaseLower));

            var valid = await app.Client.PostAsync("/json-naming/contracts",
                new StringContent("""{ "unit_price": 10, "signed_at": "2026-01-02" }""", Encoding.UTF8, "application/json"), Ct);
            valid.StatusCode.Should().Be(HttpStatusCode.OK);
            (await valid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("unit_price").GetDecimal().Should().Be(10);

            var invalid = await app.Client.PostAsync("/json-naming/contracts",
                new StringContent("""{ "unit_price": 5000 }""", Encoding.UTF8, "application/json"), Ct);
            invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
            Names(errors).Should().Contain("unit_price").And.NotContain("UnitPrice");
        }

        [Fact]
        public async Task ConfigureJson_ShouldApplyConvertersEverywhere()
        {
            await using var app = await StartAsync(s => s
                .WithJsonNaming(JsonNamingPolicy.SnakeCaseLower)
                .ConfigureJson(json => json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower))));

            (await GetJsonAsync(app, "/json-naming/contracts")).GetProperty("current_status").GetString().Should().Be("on_hold");
            (await GetJsonAsync(app, "/json-naming/minimal")).GetProperty("current_status").GetString().Should().Be("on_hold");

            var status = (await GetJsonAsync(app, "/openapi/v1.json"))
                .GetProperty("components").GetProperty("schemas").GetProperty(nameof(ContractStatus));
            status.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo("active", "on_hold");
        }

        [Fact]
        public async Task WithoutJsonNaming_ShouldKeepAspNetCoreDefaults()
        {
            await using var app = await StartAsync();

            Names(await GetJsonAsync(app, "/json-naming/contracts")).Should().Contain("unitPrice");
        }
    }
}
