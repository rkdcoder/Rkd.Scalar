using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using MvcProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Rkd.Scalar.Errors
{
    /// <summary>
    /// Writes <c>application/problem+json</c> responses through ASP.NET Core's <see cref="IProblemDetailsService"/>
    /// (so <c>CustomizeProblemDetails</c> and custom writers apply), falling back to plain JSON when no writer
    /// accepts the request (for example <c>Accept: text/html</c>): the response format never depends on the client.
    /// </summary>
    internal sealed class ProblemDetailsWriter
    {
        private const string ContentType = Rkd.Problems.HttpProblem.MediaType;

        /// <summary>RFC 9110 links used by ASP.NET Core as default <c>type</c> of each status code.</summary>
        private static readonly Dictionary<int, string> TypeLinks = new()
        {
            [400] = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            [401] = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
            [403] = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
            [404] = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
            [405] = "https://tools.ietf.org/html/rfc9110#section-15.5.6",
            [406] = "https://tools.ietf.org/html/rfc9110#section-15.5.7",
            [408] = "https://tools.ietf.org/html/rfc9110#section-15.5.9",
            [409] = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            [412] = "https://tools.ietf.org/html/rfc9110#section-15.5.13",
            [413] = "https://tools.ietf.org/html/rfc9110#section-15.5.14",
            [415] = "https://tools.ietf.org/html/rfc9110#section-15.5.16",
            [422] = "https://tools.ietf.org/html/rfc9110#section-15.5.21",
            [426] = "https://tools.ietf.org/html/rfc9110#section-15.5.22",
            [500] = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            [502] = "https://tools.ietf.org/html/rfc9110#section-15.6.3",
            [503] = "https://tools.ietf.org/html/rfc9110#section-15.6.4",
            [504] = "https://tools.ietf.org/html/rfc9110#section-15.6.5"
        };

        public async Task WriteAsync(HttpContext context, MvcProblemDetails problem, Exception? exception = null)
        {
            problem.Status ??= context.Response.StatusCode;
            context.Response.StatusCode = problem.Status.Value;

            HttpLogging.HttpLogItems.SetProblem(context, problem);

            var service = context.RequestServices.GetService<IProblemDetailsService>();

            var problemContext = new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = problem,
                Exception = exception
            };

            if (service is not null && await service.TryWriteAsync(problemContext))
                return;

            // No registered writer accepted the request: apply the same defaults and customization, then write JSON.
            problem.Title ??= ReasonPhrases.GetReasonPhrase(problem.Status.Value);

            if (problem.Type is null && TypeLinks.TryGetValue(problem.Status.Value, out var type))
                problem.Type = type;

            problem.Extensions.TryAdd(ProblemCodes.TraceIdName, Activity.Current?.Id ?? context.TraceIdentifier);

            context.RequestServices.GetService<IOptions<ProblemDetailsOptions>>()?.Value
                .CustomizeProblemDetails?.Invoke(problemContext);

            var json = context.RequestServices.GetService<IOptions<HttpJsonOptions>>()?.Value.SerializerOptions
                ?? JsonSerializerOptions.Web;

            context.Response.ContentType = ContentType;
            await JsonSerializer.SerializeAsync(context.Response.Body, problem, json, context.RequestAborted);
        }
    }
}
