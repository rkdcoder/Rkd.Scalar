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
        /// When <see langword="true"/> (default), XML documentation comments are applied to the OpenAPI documents.
        /// </summary>
        public bool XmlComments { get; set; } = true;

        /// <summary>
        /// Global options of <see cref="ApiModuleAttribute"/> controllers.
        /// </summary>
        public ApiModuleOptions ApiModules { get; } = new();

        /// <summary>
        /// Options passed to <c>UseRkdScalar</c>. Available once the application pipeline is built.
        /// </summary>
        public RkdScalarOptions Options { get; set; } = new();

        /// <summary>
        /// Customizations of the Bearer scheme (<c>ConfigureJwtBearer</c>), applied after Rkd.Scalar's settings
        /// regardless of the order of the builder calls.
        /// </summary>
        public List<Action<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>> JwtBearerConfigurations { get; } = new();

        /// <summary>Whether an <see cref="IJwtSigningKeyResolver"/> was registered.</summary>
        public bool HasJwtSigningKeyResolver { get; set; }

        public void AddAuthenticationScheme(string scheme)
        {
            if (!AuthenticationSchemes.Contains(scheme))
                AuthenticationSchemes.Add(scheme);
        }
    }
}
