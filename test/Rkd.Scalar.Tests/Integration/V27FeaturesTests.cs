using System.Formats.Asn1;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Rkd.Problems;
using Rkd.Scalar.Security.Jwt;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    public class V27FeaturesTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static readonly ClaimsIdentity User = new([new Claim(ClaimTypes.Name, "rodrigo")], "test");

        // ---------- 1. Additional audiences ----------

        [Fact]
        public async Task AdditionalAudiences_ShouldBeIssuedAsArrayAndValidatedOneByOne()
        {
            var (privatePem, publicPem) = TestKeys.EcP256();
            var options = TestJwt.Options(o =>
            {
                o.Audience = "my-app";
                o.AdditionalAudiences = ["ia.dataapi", "my-app", " "];
                o.PrivateKeyPem = privatePem;
            });

            var token = await TestJwt.Service(options).CreateTokenAsync(User, cancellationToken: Ct);
            var jwt = new JsonWebToken(token.AccessToken);

            jwt.Audiences.Should().Equal("my-app", "ia.dataapi");
            Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(token.AccessToken.Split('.')[1]))
                .Should().Contain("\"aud\":[\"my-app\",\"ia.dataapi\"]");

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicPem);

            foreach (var audience in new[] { "my-app", "ia.dataapi" })
            {
                var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.AccessToken, new TokenValidationParameters
                {
                    ValidIssuer = "issuer",
                    ValidAudience = audience,
                    IssuerSigningKey = new ECDsaSecurityKey(ecdsa)
                });

                result.IsValid.Should().BeTrue(audience);
            }
        }

        [Fact]
        public async Task SingleAudience_ShouldStayAString()
        {
            var token = await TestJwt.Service(TestJwt.Options(o => o.Secret = new string('s', 32))).CreateTokenAsync(User, cancellationToken: Ct);

            Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(token.AccessToken.Split('.')[1])).Should().Contain("\"aud\":\"audience\"");
        }

        [Fact]
        public async Task AdditionalAudiences_ShouldBeBoundFromConfigurationAndAccepted()
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithBearerAuth(),
                app => app.MapGet("/me", (ClaimsPrincipal user) => user.Identity!.Name)
                    .RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { AuthenticationSchemes = RkdScalarAuthenticationSchemes.Bearer }),
                settings: new Dictionary<string, string?>
                {
                    ["Jwt:Secret"] = new string('s', 32),
                    ["Jwt:Issuer"] = "issuer",
                    ["Jwt:Audience"] = "my-app",
                    ["Jwt:AdditionalAudiences:0"] = "ia.dataapi"
                });

            // A token issued for the other audience only (e.g. by another service).
            var other = await TestJwt.Service(TestJwt.Options(o =>
            {
                o.Secret = new string('s', 32);
                o.Audience = "ia.dataapi";
            })).CreateTokenAsync(User, cancellationToken: Ct);

            var unknown = await TestJwt.Service(TestJwt.Options(o =>
            {
                o.Secret = new string('s', 32);
                o.Audience = "someone-else";
            })).CreateTokenAsync(User, cancellationToken: Ct);

            (await GetAsync(app, "/me", other.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await GetAsync(app, "/me", unknown.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // ---------- 2. PEM private keys imported in managed code ----------

        public static TheoryData<string> PemFormats() =>
        [
            "rsa-pkcs8", "rsa-pkcs1",
            "ec256-pkcs8", "ec256-sec1", "ec256-sec1-no-public",
            "ec384-pkcs8", "ec384-sec1",
            "ec521-pkcs8", "ec521-sec1"
        ];

        [Theory]
        [MemberData(nameof(PemFormats))]
        public async Task PrivateKeyPem_ShouldBeImportedWithoutImportFromPem(string format)
        {
            var (privatePem, publicPem, expected) = CreateKey(format);

            // Decoded by the managed reader (not the ImportFromPem fallback), with the same key material.
            using (var imported = PemPrivateKeyReader.TryImport(privatePem))
            {
                imported.Should().NotBeNull(format);

                switch (imported)
                {
                    case RSA rsa:
                        rsa.ExportParameters(true).D.Should().Equal(((RSAParameters)expected).D);
                        break;
                    case ECDsa ecdsa:
                        var parameters = ecdsa.ExportParameters(true);
                        parameters.D.Should().Equal(((ECParameters)expected).D);
                        parameters.Q.X.Should().Equal(((ECParameters)expected).Q.X, "Q is read or derived from D");
                        break;
                }
            }

            var path = Path.Combine(Path.GetTempPath(), $"rkd-{Guid.NewGuid():N}.pem");
            await File.WriteAllTextAsync(path, privatePem, Ct);

            try
            {
                foreach (var options in new[]
                {
                    TestJwt.Options(o => o.PrivateKeyPem = privatePem),
                    TestJwt.Options(o => o.PrivateKeyPath = path)
                })
                {
                    var token = await TestJwt.Service(options).CreateTokenAsync(User, cancellationToken: Ct);

                    var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.AccessToken, new TokenValidationParameters
                    {
                        ValidIssuer = "issuer",
                        ValidAudience = "audience",
                        IssuerSigningKey = PublicKey(publicPem)
                    });

                    result.IsValid.Should().BeTrue(format);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void UnsupportedPem_ShouldFallBackToImportFromPem()
        {
            using var rsa = RSA.Create(2048);

            PemPrivateKeyReader.TryImport(rsa.ExportSubjectPublicKeyInfoPem()).Should().BeNull("public keys keep using ImportFromPem");
            PemPrivateKeyReader.TryImport(
                rsa.ExportEncryptedPkcs8PrivateKeyPem("pwd", new PbeParameters(PbeEncryptionAlgorithm.Aes128Cbc, HashAlgorithmName.SHA256, 1000)))
                .Should().BeNull("encrypted keys are not handled here");

            using var brainpool = ECDsa.Create(ECCurve.NamedCurves.brainpoolP256r1);
            PemPrivateKeyReader.TryImport(brainpool.ExportPkcs8PrivateKeyPem()).Should().BeNull("other curves fall back");
        }

        private static (string PrivatePem, string PublicPem, object Expected) CreateKey(string format)
        {
            if (format.StartsWith("rsa", StringComparison.Ordinal))
            {
                using var rsa = RSA.Create(2048);
                var pem = format == "rsa-pkcs8" ? rsa.ExportPkcs8PrivateKeyPem() : rsa.ExportRSAPrivateKeyPem();
                return (pem, rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportParameters(true));
            }

            var curve = format[2..5] switch
            {
                "256" => ECCurve.NamedCurves.nistP256,
                "384" => ECCurve.NamedCurves.nistP384,
                _ => ECCurve.NamedCurves.nistP521
            };

            using var ecdsa = ECDsa.Create(curve);
            var parameters = ecdsa.ExportParameters(true);

            var privatePem = format switch
            {
                _ when format.EndsWith("pkcs8", StringComparison.Ordinal) => ecdsa.ExportPkcs8PrivateKeyPem(),
                _ when format.EndsWith("no-public", StringComparison.Ordinal) => Sec1WithoutPublicKey(parameters),
                _ => ecdsa.ExportECPrivateKeyPem()
            };

            return (privatePem, ecdsa.ExportSubjectPublicKeyInfoPem(), parameters);
        }

        /// <summary>SEC1 key with only the private scalar and the curve: the public point must be derived.</summary>
        private static string Sec1WithoutPublicKey(ECParameters parameters)
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);

            using (writer.PushSequence())
            {
                writer.WriteInteger(1);
                writer.WriteOctetString(parameters.D);

                var context0 = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
                using (writer.PushSequence(context0))
                    writer.WriteObjectIdentifier(parameters.Curve.Oid.Value!);
            }

            return PemEncoding.WriteString("EC PRIVATE KEY", writer.Encode());
        }

        private static SecurityKey PublicKey(string publicPem)
        {
            try
            {
                var rsa = RSA.Create();
                rsa.ImportFromPem(publicPem);
                return new RsaSecurityKey(rsa);
            }
            catch (CryptographicException)
            {
                var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(publicPem);
                return new ECDsaSecurityKey(ecdsa);
            }
        }

        // ---------- 3. Forwarding the user's token on outgoing calls ----------

        [Fact]
        public async Task ForwardIncomingToken_ShouldForwardBearerOrFallBackToServiceToken()
        {
            var options = TestJwt.Options(o => o.Secret = new string('s', 32));
            TestServer? server = null;

            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithBearerAuth<TestCredentials, FakeCredentialValidator>(options),
                app =>
                {
                    server = (TestServer)app.Services.GetRequiredService<IServer>();

                    // The "other API": echoes what it received.
                    app.MapGet("/downstream", (HttpRequest request) => request.Headers.Authorization.ToString());

                    // This API calling the other one while handling a request.
                    app.MapGet("/call", async (IHttpClientFactory factory) =>
                        await factory.CreateClient("downstream").GetStringAsync("/downstream"));
                },
                configureBuilder: builder => builder.Services
                    .AddHttpClient("downstream", c => c.BaseAddress = new Uri("http://localhost"))
                    .ConfigurePrimaryHttpMessageHandler(() => server!.CreateHandler())
                    .AddRkdJwtToken(new ClaimsIdentity([new Claim(ClaimTypes.Name, "service")], "service"), o => o.ForwardIncomingToken = true));

            var userToken = (await TestJwt.Service(options).CreateTokenAsync(User, cancellationToken: Ct)).AccessToken;

            // 1) Request with Bearer: the user's token is forwarded as is.
            var withBearer = new HttpRequestMessage(HttpMethod.Get, "/call");
            withBearer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
            (await (await app.Client.SendAsync(withBearer, Ct)).Content.ReadAsStringAsync(Ct)).Should().Be($"Bearer {userToken}");

            // 2) Request with Basic: service token.
            var withBasic = new HttpRequestMessage(HttpMethod.Get, "/call");
            withBasic.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("a:b")));
            var basicForwarded = await (await app.Client.SendAsync(withBasic, Ct)).Content.ReadAsStringAsync(Ct);
            NameOf(basicForwarded).Should().Be("service");

            // 3) No request at all (background job): service token.
            var background = await app.Services.GetRequiredService<IHttpClientFactory>().CreateClient("downstream").GetStringAsync("/downstream", Ct);
            NameOf(background).Should().Be("service");
        }

        [Fact]
        public async Task WithoutForwardIncomingToken_ShouldAlwaysUseTheServiceToken()
        {
            var options = TestJwt.Options(o => o.Secret = new string('s', 32));
            TestServer? server = null;

            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithBearerAuth<TestCredentials, FakeCredentialValidator>(options),
                app =>
                {
                    server = (TestServer)app.Services.GetRequiredService<IServer>();
                    app.MapGet("/downstream", (HttpRequest request) => request.Headers.Authorization.ToString());
                    app.MapGet("/call", async (IHttpClientFactory factory) => await factory.CreateClient("downstream").GetStringAsync("/downstream"));
                },
                configureBuilder: builder => builder.Services
                    .AddHttpClient("downstream", c => c.BaseAddress = new Uri("http://localhost"))
                    .ConfigurePrimaryHttpMessageHandler(() => server!.CreateHandler())
                    .AddRkdJwtToken(new ClaimsIdentity([new Claim(ClaimTypes.Name, "service")], "service")));

            var request = new HttpRequestMessage(HttpMethod.Get, "/call");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", (await TestJwt.Service(options).CreateTokenAsync(User, cancellationToken: Ct)).AccessToken);

            NameOf(await (await app.Client.SendAsync(request, Ct)).Content.ReadAsStringAsync(Ct)).Should().Be("service");
        }

        private static string? NameOf(string authorization)
        {
            authorization.Should().StartWith("Bearer ");
            return new JsonWebToken(authorization["Bearer ".Length..]).GetClaim("name").Value;
        }

        // ---------- 4. Problems of other APIs ----------

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task UpstreamProblems_ShouldBeForwardedOr502(bool mapUpstream)
        {
            TestServer? server = null;
            var logs = new CollectingLoggerProvider();

            await using var app = await TestApp.StartAsync(
                scalar => scalar.WithProblemDetails(o =>
                {
                    if (mapUpstream)
                        o.MapUpstreamProblems();
                }),
                app =>
                {
                    server = (TestServer)app.Services.GetRequiredService<IServer>();

                    // The "other API".
                    app.MapGet("/other/conflict", () => RkdResults.Conflict("ORDER_LOCKED", "The order is locked."));
                    app.MapGet("/other/validation", () => RkdResults.Validation(new Dictionary<string, string[]> { ["cpf"] = ["Invalid CPF."] }));
                    app.MapGet("/other/crash", string () => throw new InvalidOperationException("db password=secret"));

                    // This API calling it.
                    app.MapGet("/proxy/{what}", async (string what, IHttpClientFactory factory) =>
                    {
                        using var response = await factory.CreateClient("other").GetAsync($"/other/{what}");
                        await response.EnsureSuccessOrThrowProblemAsync();
                        return "ok";
                    });
                },
                environment: "Production",
                configureBuilder: builder =>
                {
                    builder.Logging.AddProvider(logs);
                    builder.Services.AddHttpClient("other", c => c.BaseAddress = new Uri("http://localhost"))
                        .ConfigurePrimaryHttpMessageHandler(() => server!.CreateHandler());
                });

            var conflict = await ReadAsync(app, "/proxy/conflict");
            var validation = await ReadAsync(app, "/proxy/validation");
            var crash = await ReadAsync(app, "/proxy/crash");

            if (!mapUpstream)
            {
                conflict.Status.Should().Be(HttpStatusCode.InternalServerError);
                return;
            }

            conflict.Status.Should().Be(HttpStatusCode.Conflict);
            conflict.Body.GetProperty("code").GetString().Should().Be("ORDER_LOCKED");
            conflict.Body.GetProperty("detail").GetString().Should().Be("The order is locked.");
            conflict.Body.GetProperty("instance").GetString().Should().Be("/proxy/conflict", "the instance is this API's route");

            validation.Status.Should().Be(HttpStatusCode.BadRequest);
            validation.Body.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
            validation.Body.GetProperty("errors").GetProperty("cpf")[0].GetString().Should().Be("Invalid CPF.");

            crash.Status.Should().Be(HttpStatusCode.BadGateway);
            crash.Body.GetProperty("code").GetString().Should().Be("BAD_GATEWAY");
            crash.Text.Should().NotContain("password").And.NotContain("Internal Server Error");

            // The other API's traceId goes to the log, for correlation.
            var upstreamLog = logs.Entries.Where(e => e.Message.Contains("Upstream problem")).Select(e => e.Message).ToList();
            upstreamLog.Should().Contain(m => m.Contains("status=409") && m.Contains("code=ORDER_LOCKED") && m.Contains("traceId=00-"));
            upstreamLog.Should().Contain(m => m.Contains("status=500") && m.Contains("code=INTERNAL_SERVER_ERROR"));
        }

        private static async Task<(HttpStatusCode Status, JsonElement Body, string Text)> ReadAsync(TestApp app, string url)
        {
            var response = await app.Client.GetAsync(url, Ct);
            var text = await response.Content.ReadAsStringAsync(Ct);

            return (response.StatusCode, JsonDocument.Parse(text).RootElement, text);
        }

        private static Task<HttpResponseMessage> GetAsync(TestApp app, string url, string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return app.Client.SendAsync(request, Ct);
        }
    }
}
