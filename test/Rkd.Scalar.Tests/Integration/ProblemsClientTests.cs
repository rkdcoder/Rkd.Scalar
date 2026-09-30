using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Problems;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    /// <summary>
    /// The errors written by Rkd.Scalar, read by clients with Rkd.Problems: both sides of the same contract.
    /// </summary>
    public class ProblemsClientTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static Task<TestApp> StartAsync(Action<RkdScalarBuilder>? configure = null, Action<RkdProblemDetailsOptions>? options = null) =>
            TestApp.StartAsync(
                scalar =>
                {
                    scalar.Services.AddControllers().AddApplicationPart(typeof(JsonNamingController).Assembly);
                    scalar.WithProblemDetails(options);
                    configure?.Invoke(scalar);
                },
                app =>
                {
                    app.MapControllers();
                    app.MapGet("/pc/conflict", string () => throw RkdError.Conflict("CUSTOMER_ALREADY_EXISTS", "Duplicated document."));
                    app.MapGet("/pc/validation", string () => throw RkdError.Validation(new Dictionary<string, string[]>
                    {
                        ["email"] = ["Invalid e-mail."],
                        ["document"] = ["Required.", "Invalid format."]
                    }));
                    app.MapGet("/pc/boom", string () => throw new InvalidOperationException("boom"));
                },
                environment: "Production");

        [Fact]
        public async Task RkdError_ShouldBeReadWithItsCodeDetailAndTraceId()
        {
            await using var app = await StartAsync();

            using var response = await app.Client.GetAsync("/pc/conflict", Ct);
            var problem = await response.ReadProblemAsync(Ct);

            problem.Should().NotBeNull();
            problem!.Status.Should().Be(409);
            problem.Code.Should().Be("CUSTOMER_ALREADY_EXISTS");
            problem.Title.Should().Be("Conflict");
            problem.Detail.Should().Be("Duplicated document.");
            problem.Instance.Should().Be("/pc/conflict");
            problem.TraceId.Should().NotBeNullOrEmpty();
            problem.Extensions.Should().BeEmpty();
        }

        [Fact]
        public async Task ValidationProblem_ShouldBeReadWithItsErrors()
        {
            await using var app = await StartAsync();

            using var response = await app.Client.GetAsync("/pc/validation", Ct);
            var problem = await response.ReadProblemAsync(Ct);

            problem!.Code.Should().Be(ProblemCodes.Validation);
            problem.Errors["email"].Should().Equal("Invalid e-mail.");
            problem.Errors["document"].Should().Equal("Required.", "Invalid format.");
        }

        [Theory]
        [InlineData("/pc/boom", HttpStatusCode.InternalServerError, "INTERNAL_SERVER_ERROR")]
        [InlineData("/pc/unknown-route", HttpStatusCode.NotFound, "NOT_FOUND")]
        public async Task DefaultCodes_ShouldBeTheSameOnBothSides(string url, HttpStatusCode status, string code)
        {
            await using var app = await StartAsync();
            await using var withoutCodes = await StartAsync(options: o => o.IncludeDefaultCodes = false);

            using var response = await app.Client.GetAsync(url, Ct);
            using var responseWithoutCode = await withoutCodes.Client.GetAsync(url, Ct);

            // The server sends the code; without it, the client derives exactly the same one.
            (await response.ReadProblemAsync(Ct))!.Code.Should().Be(code);
            (await responseWithoutCode.ReadProblemAsync(Ct))!.Code.Should().Be(code);
            responseWithoutCode.StatusCode.Should().Be(status);
        }

        [Fact]
        public async Task EnsureSuccessOrThrowProblem_ShouldThrowWithTheServerDetail()
        {
            await using var app = await StartAsync();

            using var response = await app.Client.GetAsync("/pc/conflict", Ct);
            var ensure = () => response.EnsureSuccessOrThrowProblemAsync(Ct);

            var exception = (await ensure.Should().ThrowAsync<HttpProblemException>()).Which;
            exception.StatusCode.Should().Be(HttpStatusCode.Conflict);
            exception.Message.Should().Be("Duplicated document.");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SnakeCaseServer_ShouldKeepTheContractMemberNames(bool applyToDictionaryKeys)
        {
            await using var app = await StartAsync(s => s.WithJsonNaming(JsonNamingPolicy.SnakeCaseLower, applyToDictionaryKeys));

            // Controller validation (MVC), RkdError, unhandled exception and body-less 404.
            using var validation = await app.Client.PostAsync("/json-naming/contracts",
                new StringContent("""{ "unit_price": 5000 }""", Encoding.UTF8, "application/json"), Ct);
            var problem = await validation.ReadProblemAsync(Ct);

            problem!.Code.Should().Be(ProblemCodes.Validation);
            problem.TraceId.Should().NotBeNullOrEmpty();
            problem.Errors.Keys.Should().Contain("unit_price");
            problem.Extensions.Should().BeEmpty();

            foreach (var (url, code) in new[] { ("/pc/conflict", "CUSTOMER_ALREADY_EXISTS"), ("/pc/boom", "INTERNAL_SERVER_ERROR"), ("/pc/unknown", "NOT_FOUND") })
            {
                using var response = await app.Client.GetAsync(url, Ct);
                var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

                // ASP.NET Core names its trace id with the policy (trace_id): only the contract name is sent.
                body.EnumerateObject().Select(p => p.Name).Should().Contain(ProblemMembers.TraceId).And.NotContain("trace_id", url);

                using var again = await app.Client.GetAsync(url, Ct);
                var read = await again.ReadProblemAsync(Ct);

                read!.Code.Should().Be(code);
                read.TraceId.Should().NotBeNullOrEmpty();
                read.Extensions.Should().BeEmpty(url);
            }
        }

        [Fact]
        public void ContractConstants_ShouldBeTheOnesTheServerWrites()
        {
            ProblemMembers.Code.Should().Be("code");
            ProblemMembers.TraceId.Should().Be("traceId");
            ProblemMembers.Errors.Should().Be("errors");
            ProblemCodes.Validation.Should().Be("VALIDATION_ERROR");
        }

        [Fact]
        public void DefaultCodesAndTitles_ShouldFollowTheAspNetCoreReasonPhrases()
        {
            for (var status = 0; status < 1000; status++)
            {
                var phrase = ReasonPhrases.GetReasonPhrase(status);

                ProblemCodes.FromStatus(status).Should().Be(ExpectedCode(status, phrase), $"status {status}");

                if (phrase.Length > 0)
                    new HttpProblem { Status = status }.Title.Should().Be(phrase, $"status {status}");
            }
        }

        /// <summary>The rule of the default codes: the reason phrase in UPPER_SNAKE, or <c>HTTP_{status}</c>.</summary>
        private static string ExpectedCode(int status, string phrase)
        {
            if (phrase.Length == 0)
                return $"HTTP_{status}";

            var code = new StringBuilder();

            foreach (var character in phrase)
            {
                if (char.IsLetterOrDigit(character))
                    code.Append(char.ToUpperInvariant(character));
                else if (code.Length > 0 && code[^1] != '_')
                    code.Append('_');
            }

            return code.ToString().TrimEnd('_');
        }
    }
}
