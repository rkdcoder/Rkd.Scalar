using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    public class OrderNotFoundException(int id) : KeyNotFoundException($"Order {id} was not found.");

    public class PaymentGatewayException() : Exception("Gateway password=secret timed out");

    public sealed class RuleViolationException(string rule) : Exception($"Rule '{rule}' violated.")
    {
        public string Rule { get; } = rule;
    }

    public sealed class CreateOrder
    {
        [Required]
        public string? Product { get; set; }

        [Range(1, 10)]
        public int Quantity { get; set; }
    }

    [ApiController]
    [Route("problem-validation")]
    public sealed class ProblemValidationController : ControllerBase
    {
        [HttpPost]
        public IActionResult Create(CreateOrder order) => Ok(order);
    }

    public class ProblemDetailsTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static Task<TestApp> StartAsync(
            Action<RkdProblemDetailsOptions>? configure = null,
            string environment = "Production",
            Action<RkdScalarBuilder>? scalar = null)
        {
            return TestApp.StartAsync(
                builder =>
                {
                    builder.Services.AddControllers().AddApplicationPart(typeof(ProblemValidationController).Assembly);
                    builder.WithProblemDetails(configure);
                    scalar?.Invoke(builder);
                },
                app =>
                {
                    app.MapControllers();
                    app.MapGet("/boom", string () => throw new InvalidOperationException("Connection string password=secret"));
                    app.MapGet("/orders/{id:int}", string (int id) => throw new OrderNotFoundException(id));
                    app.MapGet("/payment", string () => throw new PaymentGatewayException());
                    app.MapGet("/rule", string () => throw new RuleViolationException("max-items"));
                    app.MapGet("/problem", string () => throw new ProblemException(StatusCodes.Status409Conflict, "Duplicated order", "Order 7 already exists.")
                    {
                        Type = "https://errors.example.com/duplicated-order",
                        Extensions = { ["orderId"] = 7 }
                    });
                    app.MapGet("/own-body", () => Results.BadRequest(new { message = "custom" }));
                    app.MapGet("/results-problem", () => Results.Problem(statusCode: 422, detail: "From Results.Problem"));
                    app.MapGet("/empty-404", () => Results.NotFound());
                    app.MapGet("/skip", () => Results.NotFound()).WithMetadata(new SkipStatusCodePagesAttribute());
                    app.MapPost("/json", (CreateOrder order) => order);
                    app.MapGet("/secure", () => "ok").RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = RkdScalarAuthenticationSchemes.Bearer });
                },
                environment: environment);
        }

        private static async Task<(HttpResponseMessage Response, JsonElement Body)> GetProblemAsync(
            HttpClient client, string url, string? accept = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (accept is not null)
                request.Headers.Accept.ParseAdd(accept);

            var response = await client.SendAsync(request, Ct);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
            return (response, body);
        }

        [Fact]
        public async Task UnhandledException_InProduction_ShouldHideDetails()
        {
            await using var app = await StartAsync();

            var (response, body) = await GetProblemAsync(app.Client, "/boom");

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            body.GetProperty("status").GetInt32().Should().Be(500);
            body.GetProperty("title").GetString().Should().Be("An unexpected error occurred.");
            body.GetProperty("type").GetString().Should().StartWith("https://tools.ietf.org/html/rfc9110");
            body.GetProperty("instance").GetString().Should().Be("/boom");
            body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
            body.TryGetProperty("detail", out _).Should().BeFalse();
            body.TryGetProperty("exception", out _).Should().BeFalse();
            body.ToString().Should().NotContain("secret");
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
        }

        [Fact]
        public async Task UnhandledException_InDevelopment_ShouldIncludeDetails_EvenForBrowsers()
        {
            await using var app = await StartAsync(environment: "Development");

            var (response, body) = await GetProblemAsync(app.Client, "/boom", accept: "text/html");

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            body.GetProperty("detail").GetString().Should().Contain("Connection string");
            var exception = body.GetProperty("exception");
            exception.GetProperty("type").GetString().Should().Be(typeof(InvalidOperationException).FullName);
            exception.GetProperty("stackTrace").GetString().Should().Contain("InvalidOperationException");
        }

        [Fact]
        public async Task MappedException_InDevelopment_ShouldKeepItsStatus()
        {
            await using var app = await StartAsync(o => o.Map<KeyNotFoundException>(StatusCodes.Status404NotFound), "Development");

            var (response, _) = await GetProblemAsync(app.Client, "/orders/5");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task IncludeExceptionDetailsFalse_ShouldHideDetailsEvenInDevelopment()
        {
            await using var app = await StartAsync(o => o.IncludeExceptionDetails = false, "Development");

            var (_, body) = await GetProblemAsync(app.Client, "/boom");

            body.TryGetProperty("exception", out _).Should().BeFalse();
            body.TryGetProperty("detail", out _).Should().BeFalse();
        }

        [Fact]
        public async Task MappedClientError_ShouldUseDerivedTypeMapping_AndExposeMessage()
        {
            await using var app = await StartAsync(o => o.Map<KeyNotFoundException>(StatusCodes.Status404NotFound));

            var (response, body) = await GetProblemAsync(app.Client, "/orders/5");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            body.GetProperty("title").GetString().Should().Be("Not Found");
            body.GetProperty("detail").GetString().Should().Be("Order 5 was not found.");
            body.GetProperty("instance").GetString().Should().Be("/orders/5");
        }

        [Fact]
        public async Task MostSpecificMapping_ShouldWin()
        {
            await using var app = await StartAsync(o => o
                .Map<KeyNotFoundException>(StatusCodes.Status404NotFound)
                .Map<OrderNotFoundException>(StatusCodes.Status410Gone, "Order removed"));

            var (response, body) = await GetProblemAsync(app.Client, "/orders/5");

            response.StatusCode.Should().Be(HttpStatusCode.Gone);
            body.GetProperty("title").GetString().Should().Be("Order removed");
        }

        [Fact]
        public async Task MappedServerError_ShouldNotExposeMessageByDefault()
        {
            await using var app = await StartAsync(o => o.Map<PaymentGatewayException>(StatusCodes.Status502BadGateway, "Payment provider unavailable"));

            var (response, body) = await GetProblemAsync(app.Client, "/payment");

            response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
            body.GetProperty("title").GetString().Should().Be("Payment provider unavailable");
            body.TryGetProperty("detail", out _).Should().BeFalse();
            body.ToString().Should().NotContain("secret");
        }

        [Fact]
        public async Task FactoryMapping_ShouldControlTheWholeResponse()
        {
            await using var app = await StartAsync(o => o.Map<RuleViolationException>(ex => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Business rule violated",
                Detail = ex.Message,
                Type = "https://errors.example.com/rules",
                Extensions = { ["rule"] = ex.Rule }
            }));

            var (response, body) = await GetProblemAsync(app.Client, "/rule");

            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            body.GetProperty("type").GetString().Should().Be("https://errors.example.com/rules");
            body.GetProperty("rule").GetString().Should().Be("max-items");
        }

        [Fact]
        public async Task ProblemException_ShouldBeWrittenAsIs()
        {
            await using var app = await StartAsync();

            var (response, body) = await GetProblemAsync(app.Client, "/problem");

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            body.GetProperty("title").GetString().Should().Be("Duplicated order");
            body.GetProperty("detail").GetString().Should().Be("Order 7 already exists.");
            body.GetProperty("type").GetString().Should().Be("https://errors.example.com/duplicated-order");
            body.GetProperty("orderId").GetInt32().Should().Be(7);
        }

        [Fact]
        public async Task BodylessErrorResponses_ShouldBecomeProblems()
        {
            await using var app = await StartAsync();

            var (unknown, unknownBody) = await GetProblemAsync(app.Client, "/does-not-exist");
            unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
            unknownBody.GetProperty("title").GetString().Should().Be("Not Found");

            var (empty, _) = await GetProblemAsync(app.Client, "/empty-404");
            empty.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Unauthorized_ShouldBecomeProblem_AndKeepChallengeHeader()
        {
            await using var app = await StartAsync(scalar: s => s.WithBearerAuth(TestJwt.Options(o => o.Secret = new string('s', 32))));

            var (response, body) = await GetProblemAsync(app.Client, "/secure");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.Should().Contain(h => h.Scheme == "Bearer");
            body.GetProperty("status").GetInt32().Should().Be(401);
        }

        [Fact]
        public async Task ResponsesWithBody_AndOptOuts_ShouldBeUntouched()
        {
            await using var app = await StartAsync();

            var ownBody = await app.Client.GetAsync("/own-body", Ct);
            ownBody.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ownBody.Content.ReadAsStringAsync(Ct)).Should().Contain("custom");

            var skipped = await app.Client.GetAsync("/skip", Ct);
            skipped.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await skipped.Content.ReadAsStringAsync(Ct)).Should().BeEmpty();
        }

        [Fact]
        public async Task HandleStatusCodesFalse_ShouldLeaveBodylessResponses()
        {
            await using var app = await StartAsync(o => o.HandleStatusCodes = false);

            var response = await app.Client.GetAsync("/empty-404", Ct);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync(Ct)).Should().BeEmpty();
        }

        [Fact]
        public async Task ResultsProblem_ShouldBeEnrichedAndCustomized()
        {
            await using var app = await StartAsync(o => o.Customize = ctx => ctx.ProblemDetails.Extensions["service"] = "orders-api");

            var (response, body) = await GetProblemAsync(app.Client, "/results-problem");

            response.StatusCode.Should().Be((HttpStatusCode)422);
            body.GetProperty("detail").GetString().Should().Be("From Results.Problem");
            body.GetProperty("instance").GetString().Should().Be("/results-problem");
            body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
            body.GetProperty("service").GetString().Should().Be("orders-api");
        }

        [Fact]
        public async Task ControllerValidation_ShouldBeEnriched()
        {
            await using var app = await StartAsync();

            var response = await app.Client.PostAsJsonAsync("/problem-validation", new { quantity = 99 }, Ct);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
            body.GetProperty("errors").EnumerateObject().Select(e => e.Name).Should().Contain(["Product", "Quantity"]);
            body.GetProperty("instance").GetString().Should().Be("/problem-validation");
            body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task MalformedJson_ShouldBecomeBadRequestProblem()
        {
            await using var app = await StartAsync();

            var content = new StringContent("{ not json", Encoding.UTF8, "application/json");
            var response = await app.Client.PostAsync("/json", content, Ct);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        }

        [Fact]
        public async Task BrowserAcceptHeader_ShouldStillGetJson()
        {
            await using var app = await StartAsync();

            var (response, body) = await GetProblemAsync(app.Client, "/boom", accept: "text/html");

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            body.GetProperty("status").GetInt32().Should().Be(500);
            body.GetProperty("type").GetString().Should().Be("https://tools.ietf.org/html/rfc9110#section-15.6.1");
            body.GetProperty("instance").GetString().Should().Be("/boom");
            body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void InvalidOptions_ShouldFailFast()
        {
            var options = new RkdProblemDetailsOptions();

            var map = () => options.Map<Exception>(200);
            map.Should().Throw<ArgumentOutOfRangeException>();

            var problem = () => new ProblemException(302);
            problem.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public async Task CallingTwice_ShouldFail()
        {
            var act = () => StartAsync(scalar: s => s.WithProblemDetails());

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*only be called once*");
        }
    }
}
