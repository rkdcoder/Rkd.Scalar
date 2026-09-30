namespace Rkd.Scalar
{
    /// <summary>
    /// Validation and resolution of <see cref="ApiModuleAttribute"/> route templates.
    /// </summary>
    internal static class ApiModuleTemplate
    {
        public static string NormalizeModule(string module)
        {
            if (string.IsNullOrWhiteSpace(module))
                throw new ArgumentException("The API module name cannot be empty.", nameof(module));

            var normalized = module.Trim().Trim('/');

            if (normalized.Length == 0 ||
                normalized.IndexOfAny(['{', '}', '[', ']', '?', '#', ' ', '\\']) >= 0 ||
                normalized.Contains("//", StringComparison.Ordinal))
                throw new ArgumentException(
                    $"'{module}' is not a valid API module name. Use URL path segments such as \"billing\" or \"finance/reports\".",
                    nameof(module));

            return normalized;
        }

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
