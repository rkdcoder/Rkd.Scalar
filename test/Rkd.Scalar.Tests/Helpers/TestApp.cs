using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rkd.Scalar.Tests.Helpers
{
    /// <summary>
    /// Builds an in-memory application wired with Rkd.Scalar.
    /// </summary>
    public sealed class TestApp : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private TestApp(WebApplication app)
        {
            _app = app;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public IServiceProvider Services => _app.Services;

        public static async Task<TestApp> StartAsync(
            Action<RkdScalarBuilder> configureScalar,
            Action<WebApplication>? configureApp = null,
            IDictionary<string, string?>? settings = null,
            Action<WebApplication>? useScalar = null,
            string environment = "Testing",
            Action<WebApplicationBuilder>? configureBuilder = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = environment
            });

            configureBuilder?.Invoke(builder);

            builder.WebHost.UseTestServer();

            if (settings is not null)
                builder.Configuration.AddInMemoryCollection(settings);

            builder.Services.AddAuthentication();
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(_ => { });

            configureScalar(builder.AddRkdScalar());

            var app = builder.Build();

            app.UseRateLimiter();
            app.UseAuthentication();
            app.UseAuthorization();

            configureApp?.Invoke(app);

            if (useScalar is not null)
                useScalar(app);
            else
                app.UseRkdScalar();

            await app.StartAsync(TestContext.Current.CancellationToken);

            return new TestApp(app);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
