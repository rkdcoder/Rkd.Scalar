using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rkd.Scalar.Errors;
using System.Diagnostics;

namespace Rkd.Scalar.Features
{
    internal sealed class ProblemDetailsFeature : IScalarFeature
    {
        private readonly RkdProblemDetailsOptions _options;

        public ProblemDetailsFeature(RkdProblemDetailsOptions options)
        {
            _options = options;
        }

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton(_options);
            services.TryAddSingleton<ProblemDetailsWriter>();
            services.TryAddSingleton<ProblemDetailsExceptionHandler>();

            services.AddProblemDetails();

            // Runs after every Configure<ProblemDetailsOptions>, so user customizations are kept and composed.
            services.PostConfigure<ProblemDetailsOptions>(options =>
            {
                var existing = options.CustomizeProblemDetails;

                options.CustomizeProblemDetails = context =>
                {
                    Enrich(context);
                    existing?.Invoke(context);
                    _options.Customize?.Invoke(context);
                };
            });

            services.AddTransient<IStartupFilter, ProblemDetailsStartupFilter>();
            services.AddSingleton<IDeveloperPageExceptionFilter, ProblemDetailsDeveloperPageFilter>();
        }

        public void ConfigureApp(WebApplication app)
        {
        }

        private void Enrich(ProblemDetailsContext context)
        {
            var problem = context.ProblemDetails;
            var http = context.HttpContext;

            if (_options.IncludeInstance && string.IsNullOrEmpty(problem.Instance))
                problem.Instance = http.Request.PathBase.Add(http.Request.Path).Value;

            problem.Extensions.TryAdd("traceId", Activity.Current?.Id ?? http.TraceIdentifier);
        }
    }
}
