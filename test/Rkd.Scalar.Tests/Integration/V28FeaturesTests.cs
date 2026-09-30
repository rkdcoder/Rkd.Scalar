using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    public sealed class FvAddress
    {
        public string? ZipCode { get; set; }
    }

    public sealed class FvOrder
    {
        public string? CustomerName { get; set; }

        public FvAddress? Address { get; set; }

        public List<FvItem> Items { get; set; } = [];
    }

    public sealed class FvItem
    {
        public decimal UnitPrice { get; set; }
    }

    public sealed class FvOrderValidator : AbstractValidator<FvOrder>
    {
        public FvOrderValidator()
        {
            RuleFor(o => o.CustomerName).NotEmpty();
            RuleFor(o => o.Address!.ZipCode).NotEmpty().When(o => o.Address is not null);
            RuleForEach(o => o.Items).ChildRules(item => item.RuleFor(i => i.UnitPrice).GreaterThan(0));
            RuleFor(o => o.CustomerName).MustAsync(async (name, ct) =>
            {
                await Task.Yield();
                return name != "blocked";
            }).WithMessage("Customer is blocked.");
        }
    }

    [ApiController]
    [Route("fv/orders")]
    public sealed class FvOrdersController : ControllerBase
    {
        [HttpPost]
        public IActionResult Create(FvOrder order) => Ok(new { created = order.CustomerName });

        [HttpPost("{id:int}/notes")]
        public IActionResult Note(int id, [FromBody] string note) => Ok(note);
    }

    public class V28FeaturesTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static Task<TestApp> StartAsync(bool registerValidators = true) =>
            TestApp.StartAsync(
                scalar =>
                {
                    scalar.Services.AddControllers().AddApplicationPart(typeof(FvOrdersController).Assembly);
                    scalar.WithJsonNaming(JsonNamingPolicy.SnakeCaseLower)
                        .WithProblemDetails();

                    if (registerValidators)
                        scalar.WithFluentValidation(typeof(FvOrderValidator).Assembly);
                    else
                        scalar.WithFluentValidation();
                },
                app =>
                {
                    app.MapControllers();
                    app.MapPost("/fv/minimal", (FvOrder order) => Results.Ok(new { created = order.CustomerName })).WithFluentValidation();

                    var group = app.MapGroup("/fv/group").WithFluentValidation();
                    group.MapPost("/", (FvOrder order) => Results.Ok(order.CustomerName));

                    app.MapPost("/fv/unfiltered", (FvOrder order) => Results.Ok(order.CustomerName));
                });

        private static readonly object InvalidOrder = new
        {
            customer_name = "",
            address = new { zip_code = "" },
            items = new[] { new { unit_price = 10m }, new { unit_price = 0m } }
        };

        [Theory]
        [InlineData("/fv/orders")]
        [InlineData("/fv/minimal")]
        [InlineData("/fv/group")]
        public async Task InvalidRequests_ShouldAnswerTheValidationProblem(string url)
        {
            await using var app = await StartAsync();

            var response = await app.Client.PostAsJsonAsync(url, InvalidOrder, Ct);
            var text = await response.Content.ReadAsStringAsync(Ct);
            var body = JsonDocument.Parse(text).RootElement;

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            body.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
            body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();

            var errors = body.GetProperty("errors");
            errors.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("customer_name", "address.zip_code", "items[1].unit_price");
        }

        [Fact]
        public async Task ValidRequests_AndArgumentsWithoutValidator_ShouldReachTheEndpoint()
        {
            await using var app = await StartAsync();

            var valid = new { customer_name = "Ana", address = new { zip_code = "80000-000" }, items = new[] { new { unit_price = 1m } } };

            (await app.Client.PostAsJsonAsync("/fv/orders", valid, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await app.Client.PostAsJsonAsync("/fv/minimal", valid, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await app.Client.PostAsJsonAsync("/fv/orders/1/notes", "any note", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

            // Minimal API endpoints without .WithFluentValidation() are not validated.
            (await app.Client.PostAsJsonAsync("/fv/unfiltered", InvalidOrder, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task AsyncRules_ShouldRun()
        {
            await using var app = await StartAsync();

            var response = await app.Client.PostAsJsonAsync("/fv/orders", new { customer_name = "blocked" }, Ct);
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            body.GetProperty("errors").GetProperty("customer_name")[0].GetString().Should().Be("Customer is blocked.");
        }

        [Fact]
        public async Task WithoutRegisteredValidators_NothingIsValidated()
        {
            await using var app = await StartAsync(registerValidators: false);

            (await app.Client.PostAsJsonAsync("/fv/orders", InvalidOrder, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Theory]
        [InlineData("Items[0].UnitPrice", "items[0].unit_price")]
        [InlineData("Address.ZipCode", "address.zip_code")]
        [InlineData("", "")]
        public void FieldNames_ShouldFollowTheNamingPolicy(string path, string expected) =>
            Rkd.Scalar.FluentValidation.FluentValidationRunner.ConvertPath(path, JsonNamingPolicy.SnakeCaseLower).Should().Be(expected);

        // ---------- RkdPem ----------

        [Fact]
        public void RkdPem_ShouldImportEachKind()
        {
            using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var rsa = RSA.Create(2048);

            using (var imported = RkdPem.ImportECDsaPrivateKey(ec.ExportECPrivateKeyPem()))
                imported.ExportParameters(true).D.Should().Equal(ec.ExportParameters(true).D);

            using (var imported = RkdPem.ImportRsaPrivateKey(rsa.ExportPkcs8PrivateKeyPem()))
                imported.ExportParameters(true).D.Should().Equal(rsa.ExportParameters(true).D);

            using (var any = RkdPem.ImportPrivateKey(rsa.ExportRSAPrivateKeyPem()))
                any.Should().BeAssignableTo<RSA>();

            // Encrypted keys are not handled by the managed reader nor by ImportFromPem without the password.
            var encrypted = rsa.ExportEncryptedPkcs8PrivateKeyPem("pwd", new PbeParameters(PbeEncryptionAlgorithm.Aes128Cbc, HashAlgorithmName.SHA256, 1000));
            ((Action)(() => RkdPem.ImportPrivateKey(encrypted))).Should().Throw<CryptographicException>();

            ((Action)(() => RkdPem.ImportRsaPrivateKey(ec.ExportPkcs8PrivateKeyPem()))).Should().Throw<CryptographicException>().WithMessage("*ECDSA*");
            ((Action)(() => RkdPem.ImportPrivateKey("not a pem"))).Should().Throw<CryptographicException>();
        }
    }
}
