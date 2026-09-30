using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    public sealed class TraceIdMappedException(string message) : Exception(message);

    [ApiController]
    [Route("trace-naming")]
    public sealed class TraceIdNamingController : ControllerBase
    {
        [HttpGet("mapped")]
        public string Mapped() => throw new TraceIdMappedException("Mapped in a controller.");
    }

    /// <summary>
    /// Problem details members keep their standard names (<c>traceId</c>) whatever the JSON naming policy:
    /// ASP.NET Core's default writer names the trace id with the policy (<c>trace_id</c>), which Rkd.Scalar normalizes.
    /// </summary>
    public class TraceIdNamingTests
    {
        public static TheoryData<string, string, string, string?> Cases()
        {
            var data = new TheoryData<string, string, string, string?>();

            foreach (var (policy, wrongKey) in new[] { ("snake", "trace_id"), ("kebab", "trace-id"), ("camel", (string?)null) })
            {
                data.Add(policy, "GET", "/does-not-exist", wrongKey);            // 404, body-less
                data.Add(policy, "DELETE", "/tn/get-only", wrongKey);            // 405
                data.Add(policy, "GET", "/tn/results-problem", wrongKey);        // Results.Problem
                data.Add(policy, "GET", "/tn/boom", wrongKey);                   // unhandled exception, minimal API
                data.Add(policy, "GET", "/tn/rkd-result", wrongKey);             // RkdResults, minimal API
                data.Add(policy, "GET", "/trace-naming/mapped", wrongKey);       // mapped exception, controller
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task TraceId_ShouldKeepItsStandardName(string policy, string method, string url, string? wrongKey)
        {
            var namingPolicy = policy switch
            {
                "snake" => JsonNamingPolicy.SnakeCaseLower,
                "kebab" => JsonNamingPolicy.KebabCaseLower,
                _ => JsonNamingPolicy.CamelCase
            };

            await using var app = await TestApp.StartAsync(
                scalar =>
                {
                    scalar.Services.AddControllers().AddApplicationPart(typeof(TraceIdNamingController).Assembly);
                    scalar.WithJsonNaming(namingPolicy)
                        .WithProblemDetails(o => o.Map<TraceIdMappedException>(StatusCodes.Status422UnprocessableEntity));
                },
                app =>
                {
                    app.MapControllers();
                    app.MapGet("/tn/get-only", () => "ok");
                    app.MapGet("/tn/results-problem", () => Results.Problem(statusCode: 409, detail: "Conflict."));
                    app.MapGet("/tn/boom", string () => throw new InvalidOperationException("boom"));
                    app.MapGet("/tn/rkd-result", () => RkdResults.NotFound("ITEM_NOT_FOUND"));
                },
                environment: "Production");

            var response = await app.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), TestContext.Current.CancellationToken);
            var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var body = JsonDocument.Parse(text).RootElement;

            ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(400);
            body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty(text);
            body.EnumerateObject().Count(p => p.Name == "traceId").Should().Be(1, text);
            body.TryGetProperty("code", out _).Should().BeTrue(text);

            if (wrongKey is not null)
                body.TryGetProperty(wrongKey, out _).Should().BeFalse(text);
        }
    }
}
