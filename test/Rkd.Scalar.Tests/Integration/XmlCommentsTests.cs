using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Rkd.Scalar.Tests.Helpers;
using System.Text.Json;

namespace Rkd.Scalar.Tests.Integration
{
    /// <summary>Minimal API handlers documented with XML comments.</summary>
    public static class DocumentedHandlers
    {
        /// <summary>Pings the API.</summary>
        /// <remarks>Echoes <paramref name="echo"/> back.</remarks>
        /// <param name="echo">Text to echo.</param>
        /// <returns>The echoed text.</returns>
        public static string Ping(string echo) => echo;
    }

    /// <summary>An order.</summary>
    public sealed class DocumentedOrder
    {
        /// <summary>Order identifier.</summary>
        public int Id { get; set; }

        /// <summary>Who placed the order.</summary>
        public DocumentedCustomer? Customer { get; set; }
    }

    /// <summary>A customer.</summary>
    public sealed class DocumentedCustomer
    {
        /// <summary>Customer name.</summary>
        public string Name { get; set; } = "";
    }

    [ApiController]
    [Route("orders")]
    public sealed class DocumentedOrdersController : ControllerBase
    {
        /// <summary>Gets an order.</summary>
        /// <param name="id">Order id.</param>
        /// <response code="200">The order.</response>
        /// <response code="404">Order not found.</response>
        [HttpGet("{id}")]
        [ProducesResponseType<DocumentedOrder>(200)]
        public ActionResult<DocumentedOrder> Get(int id) => new DocumentedOrder { Id = id };

        /// <summary>Creates an order.</summary>
        /// <param name="order">Order to create.</param>
        [HttpPost]
        public ActionResult<DocumentedOrder> Post([FromBody] DocumentedOrder order) => order;
    }

    public class XmlCommentsTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static Task<TestApp> StartAsync(Action<Rkd.Scalar.Builder.ScalarBuilder>? configure = null) =>
            TestApp.StartAsync(
                scalar =>
                {
                    scalar.Services.AddControllers().AddApplicationPart(typeof(DocumentedOrdersController).Assembly);
                    configure?.Invoke(scalar);
                },
                app =>
                {
                    app.MapControllers();
                    app.MapGet("/ping", DocumentedHandlers.Ping);
                });

        private static async Task<JsonElement> GetDocumentAsync(TestApp app) =>
            JsonDocument.Parse(await app.Client.GetStringAsync("/openapi/v1.json", Ct)).RootElement;

        [Fact]
        public async Task MinimalApi_ShouldGetSummaryRemarksParametersAndReturns()
        {
            await using var app = await StartAsync();
            var ping = (await GetDocumentAsync(app)).GetProperty("paths").GetProperty("/ping").GetProperty("get");

            ping.GetProperty("summary").GetString().Should().Be("Pings the API.");
            ping.GetProperty("description").GetString().Should().Be("Echoes echo back.");
            ping.GetProperty("parameters")[0].GetProperty("description").GetString().Should().Be("Text to echo.");
            ping.GetProperty("responses").GetProperty("200").GetProperty("description").GetString().Should().Be("The echoed text.");
        }

        [Fact]
        public async Task Controller_ShouldGetSummaryParametersBodyAndResponses()
        {
            await using var app = await StartAsync();
            var paths = (await GetDocumentAsync(app)).GetProperty("paths");

            var get = paths.GetProperty("/orders/{id}").GetProperty("get");
            get.GetProperty("summary").GetString().Should().Be("Gets an order.");
            get.GetProperty("parameters")[0].GetProperty("description").GetString().Should().Be("Order id.");
            get.GetProperty("responses").GetProperty("200").GetProperty("description").GetString().Should().Be("The order.");
            get.GetProperty("responses").GetProperty("404").GetProperty("description").GetString().Should().Be("Order not found.");

            var post = paths.GetProperty("/orders").GetProperty("post");
            post.GetProperty("summary").GetString().Should().Be("Creates an order.");
            post.GetProperty("requestBody").GetProperty("description").GetString().Should().Be("Order to create.");
        }

        [Fact]
        public async Task Schemas_ShouldGetTypeAndPropertyDescriptions_WithoutLeakingIntoSharedComponents()
        {
            await using var app = await StartAsync();
            var schemas = (await GetDocumentAsync(app)).GetProperty("components").GetProperty("schemas");

            var order = schemas.GetProperty("DocumentedOrder");
            order.GetProperty("description").GetString().Should().Be("An order.");
            order.GetProperty("properties").GetProperty("id").GetProperty("description").GetString().Should().Be("Order identifier.");
            order.GetProperty("properties").GetProperty("customer").ToString().Should().Contain("Who placed the order.");

            var customer = schemas.GetProperty("DocumentedCustomer");
            customer.GetProperty("description").GetString().Should().Be("A customer.");
            customer.GetProperty("properties").GetProperty("name").GetProperty("description").GetString().Should().Be("Customer name.");
        }

        [Fact]
        public async Task WithXmlCommentsFalse_ShouldNotApplyComments()
        {
            await using var app = await StartAsync(scalar => scalar.WithXmlComments(false));
            var ping = (await GetDocumentAsync(app)).GetProperty("paths").GetProperty("/ping").GetProperty("get");

            ping.TryGetProperty("summary", out _).Should().BeFalse();
        }

        [Fact]
        public async Task ExplicitSummary_ShouldNotBeOverwritten()
        {
            await using var app = await TestApp.StartAsync(
                _ => { },
                app => app.MapGet("/ping", DocumentedHandlers.Ping).WithSummary("Explicit summary"));

            var ping = (await GetDocumentAsync(app)).GetProperty("paths").GetProperty("/ping").GetProperty("get");

            ping.GetProperty("summary").GetString().Should().Be("Explicit summary");
            ping.GetProperty("parameters")[0].GetProperty("description").GetString().Should().Be("Text to echo.");
        }

        private sealed class TitleTransformer : IOpenApiDocumentTransformer
        {
            public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
            {
                document.Info.Description = "custom transformer";
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task ConfigureOpenApi_ShouldApplyToEveryDocument()
        {
            await using var app = await TestApp.StartAsync(scalar => scalar
                .WithVersioning("v1", "v2")
                .ConfigureOpenApi(o => o.AddDocumentTransformer<TitleTransformer>()));

            foreach (var version in new[] { "v1", "v2" })
            {
                using var doc = JsonDocument.Parse(await app.Client.GetStringAsync($"/openapi/{version}.json", Ct));
                doc.RootElement.GetProperty("info").GetProperty("description").GetString().Should().Be("custom transformer");
            }
        }
    }
}
