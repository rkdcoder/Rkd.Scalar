using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rkd.Scalar.FluentValidation;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Rkd.Scalar
{
    /// <summary>
    /// FluentValidation integration: invalid requests answer with the standard Rkd.Scalar validation problem
    /// (<c>errors</c> by field named with the JSON naming policy, <c>code: VALIDATION_ERROR</c>, <c>traceId</c>).
    /// </summary>
    public static class FluentValidationExtensions
    {
        /// <summary>
        /// Validates every controller action argument with its <see cref="IValidator{T}"/> before the action runs.
        /// Registers the validators found in <paramref name="assemblies"/> (scoped); validators registered elsewhere are
        /// used too. For minimal APIs, add <c>.WithFluentValidation()</c> to the endpoints or groups.
        /// </summary>
        /// <param name="builder">The Rkd.Scalar builder.</param>
        /// <param name="assemblies">Assemblies whose validators are registered (e.g. <c>typeof(Program).Assembly</c>).</param>
        /// <returns>The same builder.</returns>
        /// <example>
        /// <code>
        /// builder.AddRkdScalar()
        ///     .WithJsonNaming(JsonNamingPolicy.SnakeCaseLower)
        ///     .WithProblemDetails()
        ///     .WithFluentValidation(typeof(Program).Assembly);
        /// </code>
        /// </example>
        public static RkdScalarBuilder WithFluentValidation(this RkdScalarBuilder builder, params Assembly[] assemblies)
        {
            ArgumentNullException.ThrowIfNull(builder);

            if (assemblies is { Length: > 0 })
                builder.Services.AddValidatorsFromAssemblies(assemblies, ServiceLifetime.Scoped);

            builder.Services.Configure<MvcOptions>(options =>
            {
                if (!options.Filters.Any(f => f is TypeFilterAttribute { ImplementationType: var type } && type == typeof(FluentValidationActionFilter)))
                    options.Filters.Add<FluentValidationActionFilter>();
            });

            return builder;
        }

        /// <summary>
        /// Minimal APIs: validates the arguments of this endpoint (or of every endpoint of this group) with their
        /// registered <see cref="IValidator{T}"/> and answers <c>400</c> with the Rkd.Scalar validation problem.
        /// </summary>
        /// <typeparam name="TBuilder">Endpoint or group builder.</typeparam>
        /// <param name="builder">The endpoint builder.</param>
        /// <returns>The same builder.</returns>
        /// <example><code>app.MapGroup("/api/orders").WithFluentValidation();</code></example>
        public static TBuilder WithFluentValidation<TBuilder>(this TBuilder builder)
            where TBuilder : IEndpointConventionBuilder
        {
            ArgumentNullException.ThrowIfNull(builder);

            return builder.AddEndpointFilter(async (context, next) =>
            {
                var http = context.HttpContext;
                var namingPolicy = http.RequestServices.GetService<IOptions<HttpJsonOptions>>()?.Value.SerializerOptions.PropertyNamingPolicy;

                var errors = await FluentValidationRunner.ValidateAsync(context.Arguments, http.RequestServices, namingPolicy, http.RequestAborted);

                return errors is null ? await next(context) : RkdResults.Validation(errors);
            });
        }
    }
}
