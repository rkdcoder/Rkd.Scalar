using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Rkd.Scalar.Configuration;
using Rkd.Scalar.Extensions;
using Rkd.Scalar.Tests.Helpers;
using System.Net;

namespace Rkd.Scalar.Tests.Integration
{
    public class RealServerTests : IAsyncDisposable
    {
        private WebApplication? _app;
        private string _baseAddress = "";

        private async Task StartServerAsync()
        {
            var builder = WebApplication.CreateBuilder();

            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.Listen(IPAddress.Loopback, 0);
            });

            builder.Services.AddRkdScalar(builder.Configuration)
                   .WithUiProtection<FakeCredentialValidator>();

            _app = builder.Build();

            _app.UseRkdScalar(new RkdScalarConfiguration());

            await _app.StartAsync();

            _baseAddress = _app.Urls.First();
        }

        [Fact]
        public async Task ScalarUi_ShouldBeProtected_OnRealServer()
        {
            await StartServerAsync();

            using var client = new HttpClient { BaseAddress = new Uri(_baseAddress) };

            var response = await client.GetAsync("/scalar/v1");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            response.Headers.WwwAuthenticate.Should().Contain(h => h.Scheme == "Basic");
        }

        public async ValueTask DisposeAsync()
        {
            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }
    }
}
