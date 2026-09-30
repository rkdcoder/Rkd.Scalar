using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Rkd.Scalar.Errors
{
    /// <summary>
    /// Default machine-readable <c>code</c> of problem details responses.
    /// </summary>
    internal static class ProblemCodes
    {
        /// <summary>Extension member that carries the code.</summary>
        public const string ExtensionName = "code";

        /// <summary>Code of validation problems (<c>ValidationProblemDetails</c> / <c>HttpValidationProblemDetails</c>).</summary>
        public const string Validation = "VALIDATION_ERROR";

        /// <summary>
        /// Derives the code from the status reason phrase: 404 → <c>NOT_FOUND</c>, 429 → <c>TOO_MANY_REQUESTS</c>,
        /// 500 → <c>INTERNAL_SERVER_ERROR</c>. Unknown status codes become <c>HTTP_{status}</c>.
        /// </summary>
        public static string FromStatus(int statusCode)
        {
            var phrase = ReasonPhrases.GetReasonPhrase(statusCode);

            if (string.IsNullOrEmpty(phrase))
                return $"HTTP_{statusCode}";

            var code = new StringBuilder(phrase.Length);

            foreach (var character in phrase)
            {
                if (char.IsLetterOrDigit(character))
                    code.Append(char.ToUpperInvariant(character));
                else if (code.Length > 0 && code[^1] != '_')
                    code.Append('_');
            }

            return code.ToString().TrimEnd('_');
        }

        /// <summary>
        /// Validates a user supplied code: letters, digits, <c>_</c>, <c>-</c> and <c>.</c> only, so it is safe to switch on.
        /// </summary>
        public static string Validate(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("The error code cannot be empty.", nameof(code));

            foreach (var character in code)
            {
                if (!char.IsLetterOrDigit(character) && character is not ('_' or '-' or '.'))
                    throw new ArgumentException(
                        $"'{code}' is not a valid error code. Use letters, digits, '_', '-' or '.', e.g. \"CUSTOMER_NOT_FOUND\".",
                        nameof(code));
            }

            return code;
        }
    }
}
