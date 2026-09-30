using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Rkd.Scalar.FluentValidation
{
    /// <summary>
    /// Controllers: validates every action argument with its registered validator before the action runs and answers
    /// with the Rkd.Scalar validation problem. Runs after <c>[ApiController]</c>'s model state check, so binding and
    /// DataAnnotations errors keep answering first.
    /// </summary>
    internal sealed class FluentValidationActionFilter(IOptions<MvcJsonOptions> json) : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var errors = await FluentValidationRunner.ValidateAsync(
                context.ActionArguments.Values,
                context.HttpContext.RequestServices,
                json.Value.JsonSerializerOptions.PropertyNamingPolicy,
                context.HttpContext.RequestAborted);

            if (errors is not null)
            {
                context.Result = RkdResults.Validation(errors);
                return;
            }

            await next();
        }
    }
}
