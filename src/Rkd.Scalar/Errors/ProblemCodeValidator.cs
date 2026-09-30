namespace Rkd.Scalar.Errors
{
    /// <summary>
    /// Validates the <c>code</c> of problem details set by the application. The member name, the validation code and
    /// the default codes of each status are shared with the clients in <c>Rkd.Problems</c>
    /// (<see cref="Rkd.Problems.ProblemMembers"/>, <see cref="Rkd.Problems.ProblemCodes"/>).
    /// </summary>
    internal static class ProblemCodeValidator
    {
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
