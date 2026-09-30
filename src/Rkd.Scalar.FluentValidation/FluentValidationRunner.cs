using System.Collections.Concurrent;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;

namespace Rkd.Scalar.FluentValidation
{
    /// <summary>
    /// Runs the <see cref="IValidator{T}"/> registered for each argument and collects the failures by field, with the
    /// field names converted by the JSON naming policy (<c>Address.ZipCode</c> → <c>address.zip_code</c>), so the
    /// <c>errors</c> match the names clients send.
    /// </summary>
    internal static class FluentValidationRunner
    {
        private static readonly ConcurrentDictionary<Type, Type> ValidatorTypes = new();

        public static async Task<Dictionary<string, string[]>?> ValidateAsync(
            IEnumerable<object?> arguments,
            IServiceProvider services,
            JsonNamingPolicy? namingPolicy,
            CancellationToken cancellationToken)
        {
            List<ValidationFailure>? failures = null;

            foreach (var argument in arguments)
            {
                if (argument is null || !IsModel(argument.GetType()))
                    continue;

                var validatorType = ValidatorTypes.GetOrAdd(argument.GetType(), type => typeof(IValidator<>).MakeGenericType(type));

                if (services.GetService(validatorType) is not IValidator validator)
                    continue;

                var result = await validator.ValidateAsync(new ValidationContext<object>(argument), cancellationToken);

                if (!result.IsValid)
                    (failures ??= []).AddRange(result.Errors);
            }

            if (failures is null)
                return null;

            return failures
                .GroupBy(failure => ConvertPath(failure.PropertyName, namingPolicy), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(f => f.ErrorMessage).Distinct().ToArray(), StringComparer.Ordinal);
        }

        /// <summary>Only models are validated: framework types (HttpContext, CancellationToken, strings, numbers…) are skipped.</summary>
        private static bool IsModel(Type type) =>
            !type.IsPrimitive && type != typeof(string) && type != typeof(decimal) && !type.IsEnum &&
            type.Namespace?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) != true &&
            type.Namespace?.StartsWith("System", StringComparison.Ordinal) != true;

        /// <summary><c>Items[0].UnitPrice</c> → <c>items[0].unit_price</c> with snake_case; unchanged without a policy.</summary>
        internal static string ConvertPath(string? path, JsonNamingPolicy? policy)
        {
            if (string.IsNullOrEmpty(path) || policy is null)
                return path ?? string.Empty;

            return string.Join('.', path.Split('.').Select(segment =>
            {
                var index = segment.IndexOf('[');
                var name = index < 0 ? segment : segment[..index];
                var suffix = index < 0 ? string.Empty : segment[index..];

                return (name.Length == 0 ? name : policy.ConvertName(name)) + suffix;
            }));
        }
    }
}
