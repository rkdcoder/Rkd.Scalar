using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rkd.Scalar.Configuration;
using Rkd.Scalar.Extensions;
using Rkd.Scalar.Features;
using Rkd.Scalar.Tests.Helpers;
using FluentAssertions;

namespace Rkd.Scalar.Tests.Integration;

public class DependencyInjectionTests
{
    [Fact]
    public void AddRkdScalar_ShouldRegisterCoreServices()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddRkdScalar(config);
        var provider = services.BuildServiceProvider();

        // Verifica se o OpenApiDocumentRegistry foi registrado (core)
        provider.GetService<Rkd.Scalar.OpenApi.OpenApiDocumentRegistry>().Should().NotBeNull();
    }

    [Fact]
    public void WithUiProtection_ShouldEnableOptions()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddRkdScalar(config)
               .WithUiProtection<FakeCredentialValidator>();

        var provider = services.BuildServiceProvider();

        // Simula execução do ConfigureServices das features
        var builder = provider.GetRequiredService<Rkd.Scalar.Builder.ScalarBuilder>();
        // Reflection ou chamada interna necessária se RegisterFeature for internal, 
        // mas aqui estamos testando o efeito colateral nos Options.
        // Nota: Como 'RegisterFeature' roda imediatamente no Builder, os serviços já estão na collection.

        var options = provider.GetRequiredService<IOptions<ScalarUiProtectionOptions>>();
        options.Value.Enabled.Should().BeTrue();

        // Verifica se os validadores foram registrados
        provider.GetService<Rkd.Scalar.Security.Contracts.IUiCredentialValidator>().Should().NotBeNull();
    }
}