using Rkd.Scalar.Features;

namespace Rkd.Scalar.Infrastructure
{
    internal sealed class ScalarFeatureRegistry
    {
        public List<IScalarFeature> Features { get; } = new();
    }
}
