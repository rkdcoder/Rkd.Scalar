using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rkd.Problems;
using Rkd.Scalar.Errors;
using System.Diagnostics;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
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

            if (_options.IncludeDefaultCodes && !problem.Extensions.ContainsKey(ProblemMembers.Code))
            {
                problem.Extensions[ProblemMembers.Code] = problem is HttpValidationProblemDetails
                    ? ProblemCodes.Validation
                    : ProblemCodes.FromStatus(problem.Status ?? http.Response.StatusCode);
            }

            var traceId = Activity.Current?.Id ?? http.TraceIdentifier;

            RemovePolicyNamedTraceId(problem, http, traceId);
            problem.Extensions.TryAdd(ProblemMembers.TraceId, traceId);
        }

        /// <summary>
        /// ASP.NET Core names the trace id it adds with the JSON naming policy (<c>trace_id</c> with snake_case), while
        /// the contract member is always <c>traceId</c>: without this, the response would carry both.
        /// </summary>
        private static void RemovePolicyNamedTraceId(Microsoft.AspNetCore.Mvc.ProblemDetails problem, HttpContext http, string traceId)
        {
            var json = http.RequestServices.GetService<IOptions<HttpJsonOptions>>()?.Value.SerializerOptions;

            foreach (var policy in (ReadOnlySpan<System.Text.Json.JsonNamingPolicy?>)[json?.PropertyNamingPolicy, json?.DictionaryKeyPolicy])
            {
                var name = policy?.ConvertName(ProblemMembers.TraceId);

                if (name is not null && name != ProblemMembers.TraceId &&
                    problem.Extensions.TryGetValue(name, out var value) && Equals(value, traceId))
                {
                    problem.Extensions.Remove(name);
                }
            }
        }
    }
}
