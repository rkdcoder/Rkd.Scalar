using Rkd.Scalar.Configuration;
using Rkd.Scalar.Features;

namespace Rkd.Scalar.Infrastructure
{
    internal sealed class ScalarFeatureRegistry
    {
        public List<IScalarFeature> Features { get; } = new();

        /// <summary>
        /// Authentication schemes registered through the builder, in registration order.
        /// </summary>
        public List<string> AuthenticationSchemes { get; } = new();

        /// <summary>
        /// When <see langword="true"/>, security requirements are written per operation
        /// (only on endpoints that require authorization) instead of globally.
        /// </summary>
        public bool OperationLevelSecurity { get; set; }

        /// <summary>
        /// Configuration passed to <c>UseRkdScalar</c>. Available once the application pipeline is built.
        /// </summary>
        public RkdScalarConfiguration Configuration { get; set; } = new();

        public void AddAuthenticationScheme(string scheme)
        {
            if (!AuthenticationSchemes.Contains(scheme))
                AuthenticationSchemes.Add(scheme);
        }
    }
}
