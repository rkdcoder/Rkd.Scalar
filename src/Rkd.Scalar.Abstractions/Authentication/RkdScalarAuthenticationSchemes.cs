namespace Rkd.Scalar
{
    /// <summary>
    /// Authentication scheme names registered by Rkd.Scalar.
    /// </summary>
    /// <example>
    /// <code>[Authorize(AuthenticationSchemes = RkdScalarAuthenticationSchemes.All)]</code>
    /// </example>
    public static class RkdScalarAuthenticationSchemes
    {
        /// <summary>JWT Bearer scheme registered by <c>WithBearerAuth</c>.</summary>
        public const string Bearer = "Bearer";

        /// <summary>HTTP Basic scheme registered by <c>WithBasicAuth</c>.</summary>
        public const string Basic = "Basic";

        /// <summary>API Key scheme registered by <c>WithApiKeyAuth</c>.</summary>
        public const string ApiKey = "ApiKey";

        /// <summary>
        /// Policy scheme registered by <c>WithDefaultAuthenticationScheme</c>. It forwards each request
        /// to Bearer, Basic or ApiKey based on the credentials the request carries.
        /// </summary>
        public const string Default = "RkdScalar";

        /// <summary>Bearer, Basic and ApiKey combined, for <c>[Authorize(AuthenticationSchemes = ...)]</c>.</summary>
        public const string All = Bearer + "," + Basic + "," + ApiKey;
    }
}
