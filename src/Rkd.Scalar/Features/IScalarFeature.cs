using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rkd.Scalar.Features
{
    internal interface IScalarFeature
    {
        void ConfigureServices(IServiceCollection services, IConfiguration configuration);

        void ConfigureApp(WebApplication app);
    }
}
