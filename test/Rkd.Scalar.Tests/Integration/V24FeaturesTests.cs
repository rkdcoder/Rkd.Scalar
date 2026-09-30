using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    public sealed class UploadModel
    {
        public string? Name { get; set; }

        public IFormFile? File { get; set; }

        public List<IFormFile>? Attachments { get; set; }
    }

    [ApiController]
    [Route("v24/files")]
    public sealed class V24FilesController : ControllerBase
    {
        [HttpPost]
        public long Upload(IFormFile file) => file.Length;

        [HttpPost("form")]
        public string? UploadForm([FromForm] UploadModel model) => model.Name;
    }

    [ApiModule("tools/{toolId}")]
    public sealed class AttributesController : ControllerBase
    {
        [HttpGet]
        public string Get(int toolId) => $"tool {toolId}";
    }

    /// <summary>Exception of an existing code base: technical details only for the log.</summary>
    public sealed class LegacyAppException(string message, string details) : Exception(message), IProblemLogDetails
    {
        public string Details { get; } = details;

        string? IProblemLogDetails.LogDetails => Details;
    }

    public sealed class CollectingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(CollectingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                provider.Entries.Enqueue((logLevel, formatter(state, exception), exception));
        }
    }

    public class V24FeaturesTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static async Task<(HttpStatusCode Status, JsonElement Body, string Text)> SendAsync(TestApp app, HttpRequestMessage request)
        {
            var response = await app.Client.SendAsync(request, Ct);
            var text = await response.Content.ReadAsStringAsync(Ct);

            return (response.StatusCode, text.StartsWith('{') ? JsonDocument.Parse(text).RootElement : default, text);
        }

        private static MultipartFormDataContent Multipart(int fileSize, string field = "file")
        {
            var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(new byte[fileSize]), field, "data.bin");
            return content;
        }

        [Fact]
        public async Task MapStatus_ShouldDescribeBodylessResponses()
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithProblemDetails(o => o
                    .MapStatus(StatusCodes.Status404NotFound, "ROUTE_NOT_FOUND", "Check the route and the API version.")
                    .MapStatus(StatusCodes.Status405MethodNotAllowed, context => new ProblemDetails
                    {
                        Detail = $"{context.Request.Method} is not supported here.",
                        Extensions = { ["code"] = "METHOD_NOT_ALLOWED_HERE" }
                    })),
                app => app.MapGet("/only-get", () => "ok"));

            var (status, body, _) = await SendAsync(app, new HttpRequestMessage(HttpMethod.Get, "/nope"));
            status.Should().Be(HttpStatusCode.NotFound);
            body.GetProperty("code").GetString().Should().Be("ROUTE_NOT_FOUND");
            body.GetProperty("detail").GetString().Should().Be("Check the route and the API version.");
            body.GetProperty("title").GetString().Should().Be("Not Found");
            body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();

            var (status405, body405, _) = await SendAsync(app, new HttpRequestMessage(HttpMethod.Delete, "/only-get"));
            status405.Should().Be(HttpStatusCode.MethodNotAllowed);
            body405.GetProperty("status").GetInt32().Should().Be(405);
            body405.GetProperty("code").GetString().Should().Be("METHOD_NOT_ALLOWED_HERE");
            body405.GetProperty("detail").GetString().Should().Be("DELETE is not supported here.");
        }

        [Fact]
        public async Task LogDetails_ShouldBeLoggedButNeverSent()
        {
            var logs = new CollectingLoggerProvider();

            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithProblemDetails(o => o.Map<LegacyAppException>(StatusCodes.Status422UnprocessableEntity)),
                app =>
                {
                    app.MapGet("/rkd", string () => throw RkdError.Conflict("DUPLICATED", "Already exists.").WithLogDetails("db-row=77"));
                    app.MapGet("/legacy", string () => throw new LegacyAppException("Invalid state.", "ORA-00001 unique constraint"));
                    app.MapGet("/inner", string () => throw new ProblemException(400, detail: "Bad input.",
                        innerException: new FormatException("parser detail")));
                    app.MapGet("/server", string () => throw new ProblemException(503, detail: "Try later.") { LogDetails = "upstream=vector-store" });
                },
                configureBuilder: builder => builder.Logging.AddProvider(logs));

            var (_, rkd, rkdText) = await SendAsync(app, new HttpRequestMessage(HttpMethod.Get, "/rkd"));
            rkd.GetProperty("code").GetString().Should().Be("DUPLICATED");
            rkdText.Should().NotContain("db-row");

            var (status, _, legacyText) = await SendAsync(app, new HttpRequestMessage(HttpMethod.Get, "/legacy"));
            status.Should().Be(HttpStatusCode.UnprocessableEntity);
            legacyText.Should().NotContain("ORA-00001");

            await SendAsync(app, new HttpRequestMessage(HttpMethod.Get, "/inner"));
            var (_, _, serverText) = await SendAsync(app, new HttpRequestMessage(HttpMethod.Get, "/server"));
            serverText.Should().NotContain("vector-store");

            var entries = logs.Entries.ToArray();
            entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("db-row=77") && e.Exception == null);
            entries.Should().Contain(e => e.Message.Contains("ORA-00001 unique constraint"));
            entries.Should().Contain(e => e.Message.Contains("Bad input.") && e.Exception != null && e.Exception.InnerException is FormatException);
            entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("upstream=vector-store"));
        }

        [Fact]
        public async Task FormLimits_ShouldBe413InsteadOf500()
        {
            await using var app = await TestApp.StartAsync(
                scalar =>
                {
                    scalar.Services.AddControllers().AddApplicationPart(typeof(V24FilesController).Assembly);
                    scalar.WithProblemDetails();
                },
                app =>
                {
                    app.MapControllers();
                    app.MapPost("/raw-form", async (HttpRequest request) => (await request.ReadFormAsync()).Files.Count);
                    app.MapPost("/minimal-upload", (IFormFile file) => file.Length).DisableAntiforgery();
                },
                environment: "Production",
                configureBuilder: builder => builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 1024));

            var (rawStatus, raw, rawText) = await SendAsync(app, new HttpRequestMessage(HttpMethod.Post, "/raw-form") { Content = Multipart(4096) });
            rawStatus.Should().Be(HttpStatusCode.RequestEntityTooLarge);
            raw.GetProperty("code").GetString().Should().Be("PAYLOAD_TOO_LARGE");
            raw.GetProperty("detail").GetString().Should().Be("The request body is larger than the maximum allowed size.");
            rawText.Should().NotContain("1024");

            var (mvcStatus, mvc, _) = await SendAsync(app, new HttpRequestMessage(HttpMethod.Post, "/v24/files") { Content = Multipart(4096) });
            mvcStatus.Should().Be(HttpStatusCode.RequestEntityTooLarge);
            mvc.GetProperty("code").GetString().Should().Be("PAYLOAD_TOO_LARGE");
            mvc.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();

            // Minimal API parameter binding: outside Development ASP.NET Core answers 400 without telling why.
            var (minimalStatus, _, _) = await SendAsync(app, new HttpRequestMessage(HttpMethod.Post, "/minimal-upload") { Content = Multipart(4096) });
            minimalStatus.Should().Be(HttpStatusCode.BadRequest);

            var (okStatus, _, _) = await SendAsync(app, new HttpRequestMessage(HttpMethod.Post, "/v24/files") { Content = Multipart(10) });
            okStatus.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task FormLimits_InDevelopment_MinimalApiBindingShouldBe413()
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithProblemDetails(),
                app => app.MapPost("/minimal-upload", (IFormFile file) => file.Length).DisableAntiforgery(),
                environment: "Development",
                configureBuilder: builder => builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 1024));

            var (status, body, _) = await SendAsync(app, new HttpRequestMessage(HttpMethod.Post, "/minimal-upload") { Content = Multipart(4096) });
            status.Should().Be(HttpStatusCode.RequestEntityTooLarge);
            body.GetProperty("code").GetString().Should().Be("PAYLOAD_TOO_LARGE");
        }

        [Fact]
        public async Task InvalidDataException_FromYourCode_ShouldStill500()
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithProblemDetails(),
                app => app.MapGet("/decompress", string () => throw new InvalidDataException("Invalid gzip header limit")),
                environment: "Production");

            (await SendAsync(app, new HttpRequestMessage(HttpMethod.Get, "/decompress"))).Status.Should().Be(HttpStatusCode.InternalServerError);
        }

        [Fact]
        public async Task FormFiles_ShouldBeBinaryInOpenApi()
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar.Services.AddControllers().AddApplicationPart(typeof(V24FilesController).Assembly),
                app =>
                {
                    app.MapControllers();
                    app.MapPost("/v24/upload", (IFormFile file) => file.Length).DisableAntiforgery();
                    app.MapPost("/v24/upload-many", (IFormFileCollection files) => files.Count).DisableAntiforgery();
                });

            var json = await app.Client.GetStringAsync("/openapi/v1.json", Ct);
            var document = JsonDocument.Parse(json).RootElement;
            var paths = document.GetProperty("paths");

            JsonElement Content(string path) =>
                paths.GetProperty(path).GetProperty("post").GetProperty("requestBody").GetProperty("content");

            var single = Content("/v24/upload").GetProperty("multipart/form-data").GetProperty("schema").GetProperty("properties").GetProperty("file");
            single.GetProperty("type").GetString().Should().Be("string");
            single.GetProperty("format").GetString().Should().Be("binary");

            Content("/v24/upload-many").GetProperty("multipart/form-data").GetProperty("schema").GetProperty("properties")
                .GetProperty("files").GetProperty("items").GetProperty("format").GetString().Should().Be("binary");

            var form = Content("/v24/files/form");
            form.TryGetProperty("multipart/form-data", out var multipart).Should().BeTrue("files cannot be sent url-encoded");
            multipart.GetProperty("schema").ToString().Should().Contain("\"binary\"");

            json.Should().NotContain("#/components/schemas/IFormFile");
            document.GetProperty("components").GetProperty("schemas").TryGetProperty("IFormFile", out _).Should().BeFalse();
        }

        [Fact]
        public async Task ApiModule_ShouldAcceptRouteParameters()
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar.Services.AddControllers().AddApplicationPart(typeof(AttributesController).Assembly),
                app => app.MapControllers());

            (await app.Client.GetStringAsync("/api/tools/7/attributes", Ct)).Should().Be("tool 7");

            var document = JsonDocument.Parse(await app.Client.GetStringAsync("/openapi/v1.json", Ct)).RootElement;
            var operation = document.GetProperty("paths").EnumerateObject()
                .Single(p => p.Name.Equals("/api/tools/{toolId}/attributes", StringComparison.OrdinalIgnoreCase)).Value.GetProperty("get");
            operation.GetProperty("tags")[0].GetString().Should().Be("tools");
        }

        [Theory]
        [InlineData("tools/{toolId:int}")]
        [InlineData("tenants/{tenant}/billing")]
        public void ApiModule_ValidParameterNames(string module) =>
            new ApiModuleAttribute(module).Module.Should().Be(module);

        [Theory]
        [InlineData("{toolId}")]
        [InlineData("tools/{toolId?}")]
        [InlineData("tools/{*rest}")]
        [InlineData("tools/x{id}")]
        public void ApiModule_InvalidParameterNames(string module)
        {
            var act = () => new ApiModuleAttribute(module);
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public async Task DarkMode_ShouldComeFromOptions()
        {
            await using var dark = await TestApp.StartAsync(_ => { }, useScalar: app => app.UseRkdScalar(o => o.DarkMode = true));
            await using var light = await TestApp.StartAsync(_ => { }, useScalar: app => app.UseRkdScalar(o => o.DarkMode = false));

            var darkPage = await dark.Client.GetStringAsync("/scalar/", Ct);
            var lightPage = await light.Client.GetStringAsync("/scalar/", Ct);

            darkPage.Should().Contain("\"darkMode\":true");
            lightPage.Should().Contain("\"darkMode\":false");
        }
    }
}
