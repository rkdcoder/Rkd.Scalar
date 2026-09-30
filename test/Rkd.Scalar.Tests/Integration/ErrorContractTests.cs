using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    public static class CustomerErrors
    {
        public const string NotFound = "CUSTOMER_NOT_FOUND";
        public const string AlreadyExists = "CUSTOMER_ALREADY_EXISTS";
    }

    public sealed class CustomerDto
    {
        public int Id { get; set; }

        public int Age { get; set; }
    }

    [ApiController]
    [Route("error-contract/customers")]
    public sealed class ErrorContractController : ControllerBase
    {
        [HttpGet("{id:int}")]
        [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<CustomerDto> Get(int id) =>
            id == 1 ? new CustomerDto { Id = 1 } : RkdResults.NotFound(CustomerErrors.NotFound, $"Customer {id} was not found.");

        [HttpDelete("{id:int}")]
        public IActionResult Delete(int id) =>
            RkdResults.Conflict(CustomerErrors.AlreadyExists, "Customer has open contracts.");

        [HttpPost]
        public CustomerDto Create(CustomerDto customer) => customer;
    }

    public class ErrorContractTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static Task<TestApp> StartAsync(
            Action<RkdProblemDetailsOptions>? configure = null,
            string environment = "Production",
            bool problemDetails = true)
        {
            return TestApp.StartAsync(
                scalar =>
                {
                    scalar.Services.AddControllers().AddApplicationPart(typeof(ErrorContractController).Assembly);
                    scalar.WithBearerAuth(TestJwt.Options(o => o.Secret = new string('s', 32)));

                    if (problemDetails)
                        scalar.WithProblemDetails(configure);
                },
                app =>
                {
                    app.MapControllers();
                    app.MapGet("/ec/conflict", string () => throw RkdError.Conflict(CustomerErrors.AlreadyExists, "Duplicated document."));
                    app.MapGet("/ec/validation", string () => throw RkdError.Validation(new Dictionary<string, string[]>
                    {
                        ["email"] = ["Invalid e-mail."],
                        ["document"] = ["Required.", "Invalid format."]
                    }));
                    app.MapGet("/ec/boom", string () => throw new InvalidOperationException("boom"));
                    app.MapGet("/ec/result/{id:int}", Results<Ok<CustomerDto>, RkdProblemResult> (int id) =>
                        id == 1 ? TypedResults.Ok(new CustomerDto { Id = 1 }) : RkdResults.NotFound(CustomerErrors.NotFound));
                    app.MapGet("/ec/iresult", IResult () => RkdResults.UnprocessableEntity("LIMIT_EXCEEDED", "Credit limit exceeded."));
                    app.MapPost("/ec/json", (CustomerDto customer) => customer);
                    app.MapGet("/ec/secure", () => "ok")
                        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = RkdScalarAuthenticationSchemes.Bearer });
                    app.MapGet("/ec/limited", () => "ok").RequireRateLimiting("ec-policy");
                    app.MapGet("/ec/plain", () => "ok");
                },
                environment: environment,
                configureBuilder: builder => builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("ec-policy", l =>
                {
                    l.PermitLimit = 100;
                    l.Window = TimeSpan.FromMinutes(1);
                })));
        }

        private static async Task<(HttpStatusCode Status, JsonElement Body, string? ContentType)> SendAsync(
            TestApp app, HttpMethod method, string url, string? json = null)
        {
            var request = new HttpRequestMessage(method, url);

            if (json is not null)
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await app.Client.SendAsync(request, Ct);
            var text = await response.Content.ReadAsStringAsync(Ct);

            return (response.StatusCode, text.Length > 0 ? JsonDocument.Parse(text).RootElement : default,
                response.Content.Headers.ContentType?.MediaType);
        }

        private static string? Code(JsonElement body) =>
            body.TryGetProperty("code", out var code) ? code.GetString() : null;

        [Fact]
        public async Task RkdError_ShouldCarryItsCode()
        {
            await using var app = await StartAsync();

            var (status, body, contentType) = await SendAsync(app, HttpMethod.Get, "/ec/conflict");

            status.Should().Be(HttpStatusCode.Conflict);
            contentType.Should().Be("application/problem+json");
            Code(body).Should().Be(CustomerErrors.AlreadyExists);
            body.GetProperty("title").GetString().Should().Be("Conflict");
            body.GetProperty("detail").GetString().Should().Be("Duplicated document.");
            body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task RkdErrorValidation_ShouldMatchTheAspNetValidationShape()
        {
            await using var app = await StartAsync();

            var (status, body, _) = await SendAsync(app, HttpMethod.Get, "/ec/validation");

            status.Should().Be(HttpStatusCode.BadRequest);
            Code(body).Should().Be("VALIDATION_ERROR");
            body.GetProperty("title").GetString().Should().Be("One or more validation errors occurred.");
            body.GetProperty("errors").GetProperty("document").GetArrayLength().Should().Be(2);
        }

        [Theory]
        [InlineData("GET", "/ec/boom", null, 500, "INTERNAL_SERVER_ERROR")]
        [InlineData("GET", "/does-not-exist", null, 404, "NOT_FOUND")]
        [InlineData("GET", "/ec/secure", null, 401, "UNAUTHORIZED")]
        [InlineData("POST", "/error-contract/customers", """{ "age": 999999999999 }""", 400, "VALIDATION_ERROR")]
        [InlineData("POST", "/ec/json", "{ not json", 400, "BAD_REQUEST")]
        public async Task EveryProblem_ShouldHaveADefaultCode(string method, string url, string? json, int expectedStatus, string expectedCode)
        {
            await using var app = await StartAsync();

            var (status, body, contentType) = await SendAsync(app, new HttpMethod(method), url, json);

            ((int)status).Should().Be(expectedStatus);
            contentType.Should().Be("application/problem+json");
            Code(body).Should().Be(expectedCode);
        }

        [Fact]
        public async Task IncludeDefaultCodesFalse_ShouldKeepOnlyExplicitCodes()
        {
            await using var app = await StartAsync(o => o.IncludeDefaultCodes = false);

            Code((await SendAsync(app, HttpMethod.Get, "/does-not-exist")).Body).Should().BeNull();
            Code((await SendAsync(app, HttpMethod.Get, "/ec/conflict")).Body).Should().Be(CustomerErrors.AlreadyExists);
        }

        [Theory]
        [InlineData("GET", "/error-contract/customers/7", 404, CustomerErrors.NotFound)]
        [InlineData("DELETE", "/error-contract/customers/7", 409, CustomerErrors.AlreadyExists)]
        [InlineData("GET", "/ec/result/7", 404, CustomerErrors.NotFound)]
        [InlineData("GET", "/ec/iresult", 422, "LIMIT_EXCEEDED")]
        public async Task RkdResults_ShouldWorkInControllersAndMinimalApis(string method, string url, int expectedStatus, string expectedCode)
        {
            await using var app = await StartAsync();

            var (status, body, contentType) = await SendAsync(app, new HttpMethod(method), url);

            ((int)status).Should().Be(expectedStatus);
            contentType.Should().Be("application/problem+json");
            Code(body).Should().Be(expectedCode);
            body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
            body.GetProperty("instance").GetString().Should().Be(url);
        }

        [Fact]
        public async Task RkdResults_ShouldKeepSuccessResponses()
        {
            await using var app = await StartAsync();

            (await SendAsync(app, HttpMethod.Get, "/error-contract/customers/1")).Status.Should().Be(HttpStatusCode.OK);
            (await SendAsync(app, HttpMethod.Get, "/ec/result/1")).Body.GetProperty("id").GetInt32().Should().Be(1);
        }

        [Fact]
        public async Task RkdResults_ShouldWorkWithoutWithProblemDetails()
        {
            await using var app = await StartAsync(problemDetails: false);

            var (status, body, contentType) = await SendAsync(app, HttpMethod.Get, "/ec/result/7");

            status.Should().Be(HttpStatusCode.NotFound);
            contentType.Should().Be("application/problem+json");
            Code(body).Should().Be(CustomerErrors.NotFound);
        }

        [Fact]
        public async Task JsonConversionErrors_ShouldHideDotNetTypesOutsideDevelopment()
        {
            const string invalidAge = """{ "age": "not-a-number" }""";

            await using (var production = await StartAsync())
            {
                var (status, body, _) = await SendAsync(production, HttpMethod.Post, "/error-contract/customers", invalidAge);

                status.Should().Be(HttpStatusCode.BadRequest);
                body.GetProperty("errors").ToString().Should().NotContain("System.");

                var (minimalStatus, minimal, _) = await SendAsync(production, HttpMethod.Post, "/ec/json", invalidAge);
                minimalStatus.Should().Be(HttpStatusCode.BadRequest);
                minimal.ToString().Should().NotContain("System.");
            }

            await using var development = await StartAsync(environment: "Development");
            var (_, devBody, _) = await SendAsync(development, HttpMethod.Post, "/error-contract/customers", invalidAge);
            devBody.GetProperty("errors").ToString().Should().Contain("System.Int32");

            var (_, devMinimal, _) = await SendAsync(development, HttpMethod.Post, "/ec/json", invalidAge);
            devMinimal.GetProperty("detail").GetString().Should().Contain("CustomerDto");
        }

        [Fact]
        public void InvalidCodes_ShouldFailFast()
        {
            var empty = () => RkdError.NotFound("");
            var spaces = () => RkdError.Conflict("ALREADY EXISTS");

            empty.Should().Throw<ArgumentException>();
            spaces.Should().Throw<ArgumentException>();
        }

        private static async Task<JsonElement> OpenApiAsync(TestApp app) =>
            JsonDocument.Parse(await app.Client.GetStringAsync("/openapi/v1.json", Ct)).RootElement;

        private static string[] ResponseCodes(JsonElement document, string path, string method) =>
            document.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses")
                .EnumerateObject().Select(r => r.Name).ToArray();

        [Fact]
        public async Task OpenApi_ShouldDocumentOnlyInferableErrorResponses()
        {
            await using var app = await StartAsync();
            var document = await OpenApiAsync(app);

            ResponseCodes(document, "/ec/plain", "get").Should().BeEquivalentTo("200", "500");
            ResponseCodes(document, "/ec/secure", "get").Should().BeEquivalentTo("200", "401", "403", "500");
            ResponseCodes(document, "/ec/limited", "get").Should().BeEquivalentTo("200", "429", "500");
            ResponseCodes(document, "/ec/json", "post").Should().Contain(["400", "500"]).And.NotContain(["401", "404"]);

            var getCustomer = document.GetProperty("paths").GetProperty("/error-contract/customers/{id}").GetProperty("get").GetProperty("responses");
            getCustomer.EnumerateObject().Select(r => r.Name).Should().Contain(["404", "400", "500"]);
            getCustomer.GetProperty("404").GetProperty("content").GetProperty("application/problem+json")
                .GetProperty("schema").GetProperty("$ref").GetString().Should().EndWith("/ProblemDetails");

            var badRequest = document.GetProperty("paths").GetProperty("/ec/json").GetProperty("post").GetProperty("responses").GetProperty("400");
            badRequest.GetProperty("description").GetString().Should().Contain("VALIDATION_ERROR");
            badRequest.GetProperty("content").GetProperty("application/problem+json")
                .GetProperty("schema").GetProperty("$ref").GetString().Should().EndWith("/HttpValidationProblemDetails");
        }

        [Fact]
        public async Task OpenApi_ShouldDescribeCodeAndTraceIdInTheProblemSchemas()
        {
            await using var app = await StartAsync();
            var schemas = (await OpenApiAsync(app)).GetProperty("components").GetProperty("schemas");

            foreach (var name in new[] { "ProblemDetails", "HttpValidationProblemDetails" })
            {
                var properties = schemas.GetProperty(name).GetProperty("properties");
                properties.TryGetProperty("code", out _).Should().BeTrue(name);
                properties.TryGetProperty("traceId", out _).Should().BeTrue(name);
            }

            schemas.GetProperty("HttpValidationProblemDetails").GetProperty("properties").TryGetProperty("errors", out _).Should().BeTrue();
        }

        [Fact]
        public async Task DocumentErrorResponsesFalse_ShouldLeaveOpenApiUntouched()
        {
            await using var app = await StartAsync(o => o.DocumentErrorResponses = false);

            ResponseCodes(await OpenApiAsync(app), "/ec/plain", "get").Should().BeEquivalentTo("200");
        }
    }
}
