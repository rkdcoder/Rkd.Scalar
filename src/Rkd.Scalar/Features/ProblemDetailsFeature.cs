using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Rkd.Scalar.Errors;
using System.Diagnostics;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

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

            // Controllers: JSON conversion errors ("The JSON value could not be converted to System.Int32…")
            // expose .NET types; outside Development they become generic messages, like the other details.
            services.AddOptions<MvcJsonOptions>()
                .Configure<IHostEnvironment>((json, environment) =>
                    json.AllowInputFormatterExceptionMessages = _options.IncludeExceptionDetails ?? environment.IsDevelopment());

            // Controllers: exceeded form limits become 413 (MVC reports them as validation errors).
            services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(mvc => mvc.Filters.Add(new FormLimitActionFilter()));

            if (_options.DocumentErrorResponses)
            {
                services.ConfigureAll<OpenApiOptions>(options =>
                {
                    options.AddOperationTransformer<ErrorResponsesOperationTransformer>();
                    options.AddDocumentTransformer<ErrorResponsesDocumentTransformer>();
                });
            }

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

            if (_options.IncludeDefaultCodes && !problem.Extensions.ContainsKey(ProblemCodes.ExtensionName))
            {
                problem.Extensions[ProblemCodes.ExtensionName] = problem is HttpValidationProblemDetails
                    ? ProblemCodes.Validation
                    : ProblemCodes.FromStatus(problem.Status ?? http.Response.StatusCode);
            }

            problem.Extensions.TryAdd("traceId", Activity.Current?.Id ?? http.TraceIdentifier);

            HttpLogging.HttpLogItems.SetProblem(http, problem);
        }
    }
}
