namespace Rkd.Scalar
{
    /// <summary>
    /// Validation and resolution of <see cref="ApiModuleAttribute"/> route templates.
    /// </summary>
    internal static class ApiModuleTemplate
    {
        /// <summary>Route parameter segment: <c>{toolId}</c> or <c>{toolId:int}</c> (no optional or catch-all parameters).</summary>
        private static readonly System.Text.RegularExpressions.Regex ParameterSegment =
            new(@"^\{[A-Za-z_][A-Za-z0-9_]*(:[^{}/?*]+)?\}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        public static string NormalizeModule(string module)
        {
            if (string.IsNullOrWhiteSpace(module))
                throw new ArgumentException("The API module name cannot be empty.", nameof(module));

            var normalized = module.Trim().Trim('/');

            var segments = normalized.Split('/');

            // At least one literal segment names the module (and becomes its tag).
            if (normalized.Length == 0 ||
                segments.Any(segment => !IsValidSegment(segment)) ||
                segments.All(segment => segment.StartsWith('{')))
                throw new ArgumentException(
                    $"'{module}' is not a valid API module name. Use URL path segments such as \"billing\", " +
                    "\"finance/reports\" or \"tools/{toolId}\".",
                    nameof(module));

            return normalized;
        }

        /// <summary>
        /// Default tag of a module: its literal segments (<c>tools/{toolId}</c> → <c>tools</c>).
        /// </summary>
        public static string DefaultTag(string module)
        {
            return string.Join('/', module.Split('/').Where(segment => !segment.StartsWith('{')));
        }

        private static bool IsValidSegment(string segment) =>
            segment.Length > 0 &&
            (ParameterSegment.IsMatch(segment) || segment.IndexOfAny(['{', '}', '[', ']', '?', '#', ' ', '\\', '*']) < 0);

        public static void Validate(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
                throw new ArgumentException("The API module route template cannot be empty.", nameof(template));

            if (!template.Contains(ApiModuleAttribute.ModuleToken, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"The API module route template '{template}' must contain the {ApiModuleAttribute.ModuleToken} token.",
                    nameof(template));
        }

        public static string Resolve(string template, string module)
        {
            Validate(template);

            return template.Replace(ApiModuleAttribute.ModuleToken, module, StringComparison.OrdinalIgnoreCase);
        }
    }
}
