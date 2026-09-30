namespace Rkd.Scalar.Errors
{
    /// <summary>
    /// Members and default codes of the problem details contract. The names and codes come from Rkd.Problems, the
    /// client side of the contract, so the server and its clients share one definition.
    /// </summary>
    internal static class ProblemCodes
    {
        /// <summary>Extension member that carries the code (<c>code</c>).</summary>
        public const string ExtensionName = Rkd.Problems.ProblemMembers.Code;

        /// <summary>Extension member that carries the trace id (<c>traceId</c>).</summary>
        public const string TraceIdName = Rkd.Problems.ProblemMembers.TraceId;

        /// <summary>Extension member with the validation messages by field (<c>errors</c>).</summary>
        public const string ErrorsName = Rkd.Problems.ProblemMembers.Errors;

        /// <summary>Code of validation problems (<c>VALIDATION_ERROR</c>).</summary>
        public const string Validation = Rkd.Problems.ProblemCodes.Validation;

        /// <summary>
        /// Derives the code from the status reason phrase: 404 → <c>NOT_FOUND</c>, 429 → <c>TOO_MANY_REQUESTS</c>,
        /// 500 → <c>INTERNAL_SERVER_ERROR</c>. Unknown status codes become <c>HTTP_{status}</c>.
        /// </summary>
        public static string FromStatus(int statusCode) => Rkd.Problems.ProblemCodes.FromStatus(statusCode);

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
