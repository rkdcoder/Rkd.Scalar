namespace Rkd.Scalar.Configuration
{
    public sealed class RkdScalarConfiguration
    {
        public string OpenApiRoutePattern { get; set; } = "/openapi/{documentName}.json";

        public string Title { get; set; } = "API Documentation";
    }
}
