using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Rkd.Scalar.Infrastructure
{
    /// <summary>
    /// Turns an invalid attribute route (e.g. an action parameter <c>{version}</c> under the <c>api/v{version:apiVersion}</c>
    /// prefix of <see cref="ApiModuleAttribute"/>, which repeats the parameter name) into a startup failure that names the
    /// action, the template and how to fix it.
    /// </summary>
    /// <remarks>
    /// ASP.NET Core rejects such a template at one of two moments, never saying which action is wrong:
    /// <list type="bullet">
    /// <item>on API controllers with a simple parameter to infer, while it builds the controllers inside <c>MapControllers</c>
    /// (<see cref="RouteValidationApplicationModelProvider"/> explains it there);</item>
    /// <item>otherwise only when it builds the route table, on the first request: the application starts and every endpoint
    /// answers 500 (<see cref="RouteValidationStartupFilter"/> builds the table at startup and explains it).</item>
    /// </list>
    /// Nothing changes for an application whose routes are valid: both only run the parse ASP.NET Core would run anyway.
    /// </remarks>
    internal static partial class RouteTemplateValidation
    {
        internal const string LoggerCategory = "Rkd.Scalar.Routing";

        /// <summary>Builds every endpoint of <paramref name="services"/>; see the class remarks.</summary>
        /// <exception cref="InvalidOperationException">An attribute route template is invalid.</exception>
        public static void EnsureEndpointsCanBeBuilt(IServiceProvider services)
        {
            if (services.GetService<EndpointDataSource>() is not { } dataSource)
                return;

            try
            {
                _ = dataSource.Endpoints;
            }
            catch (Exception exception)
            {
                ThrowIfTemplatesAreInvalid(services, exception);

                // Not an invalid template: ASP.NET Core keeps its behavior (it tries again on the next request), with a trail.
                services.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategory).LogWarning(exception,
                    "Rkd.Scalar: the endpoints of the application could not be built at startup. Requests may fail until the cause is fixed.");
            }
        }

        /// <summary>
        /// When <paramref name="buildFailure"/> comes from invalid attribute routes, throws the explanation (with the
        /// original exception as the inner one). Returns otherwise, so the caller keeps the original failure.
        /// </summary>
        public static void ThrowIfTemplatesAreInvalid(IServiceProvider services, Exception buildFailure)
        {
            if (!IsRoutePatternFailure(buildFailure))
                return;

            var problems = FindInvalidTemplates(services);

            if (problems.Count > 0)
                throw new InvalidOperationException(Describe(problems), buildFailure);
        }

        private static bool IsRoutePatternFailure(Exception? exception) => exception switch
        {
            null => false,
            RoutePatternException => true,
            AggregateException aggregate => aggregate.InnerExceptions.Any(IsRoutePatternFailure),
            _ => IsRoutePatternFailure(exception.InnerException)
        };

        internal sealed record InvalidTemplate(string Action, string Template, string Error, string? Hint);

        internal static List<InvalidTemplate> FindInvalidTemplates(IServiceProvider services)
        {
            var problems = new List<InvalidTemplate>();

            if (services.GetService<IActionDescriptorCollectionProvider>() is not { } provider)
                return problems;

            IReadOnlyList<Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor> actions;

            try
            {
                actions = provider.ActionDescriptors.Items;
            }
            catch (Exception)
            {
                return problems;   // MVC itself could not list the actions: not a template problem this check can explain.
            }

            foreach (var action in actions)
            {
                if (action.AttributeRouteInfo?.Template is { } template && ParseError(template) is { } error)
                    problems.Add(new InvalidTemplate(action.DisplayName ?? action.Id, template, error, Hint(template)));
            }

            return problems;
        }

        /// <summary>The error of the same parse ASP.NET Core runs on the template, or <see langword="null"/> when it is valid.</summary>
        internal static string? ParseError(string template)
        {
            try
            {
                RoutePatternFactory.Parse(template);
                return null;
            }
            catch (RoutePatternException exception)
            {
                return exception.Message;
            }
        }

        /// <param name="problems">The invalid templates.</param>
        /// <param name="whileBuildingControllers">Found while the controllers are built (inside <c>MapControllers</c>), not when the routes are.</param>
        internal static string Describe(IReadOnlyList<InvalidTemplate> problems, bool whileBuildingControllers = false)
        {
            var text = new StringBuilder()
                .Append("Rkd.Scalar: ").Append(problems.Count)
                .Append(problems.Count == 1 ? " route template is invalid" : " route templates are invalid")
                .AppendLine(whileBuildingControllers
                    ? ". ASP.NET Core would stop here as well (while building the controllers, in MapControllers), but without naming the action or the template; on IIS every request answers 500:"
                    : ". ASP.NET Core only builds the route table on the first request, so without this check the application would start and answer 500 on EVERY endpoint:");

            foreach (var problem in problems)
            {
                text.Append(" - ").Append(problem.Action).Append(": '").Append(problem.Template).AppendLine("'")
                    .Append("   ").AppendLine(problem.Error);

                if (problem.Hint is not null)
                    text.Append("   ").AppendLine(problem.Hint);
            }

            return text.Append("Fix the route(s) above. This check runs at startup and can be turned off with WithRouteValidation(false).").ToString();
        }

        /// <summary>What to do about the most common mistake, a parameter name used twice in the combined template.</summary>
        internal static string? Hint(string template)
        {
            var repeated = ParameterRegex().Matches(template)
                .Select(m => m.Groups["name"].Value.Trim())
                .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1)?.Key;

            if (repeated is null)
                return null;

            return template.Contains($"{{{repeated}:apiVersion}}", StringComparison.OrdinalIgnoreCase)
                ? $"'{{{repeated}}}' is already the API version segment ('{{{repeated}:apiVersion}}', from [ApiModule] / WithApiModules or the controller route): give the action's parameter another name, e.g. '{{package{char.ToUpperInvariant(repeated[0])}{repeated[1..]}}}'."
                : $"The parameter '{{{repeated}}}' appears more than once in the combined route (controller + action): give one of them another name.";
        }

        // {name}, {name:constraint}, {name=default}, {name?}, {*name}, {**name}; "{{" is an escaped brace, not a parameter.
        [GeneratedRegex(@"(?<!\{)\{(?!\{)\**(?<name>[^{}*?:=]+)")]
        private static partial Regex ParameterRegex();
    }
}
