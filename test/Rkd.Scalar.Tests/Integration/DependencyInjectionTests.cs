using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rkd.Scalar.Builder;
using Rkd.Scalar.Configuration;
using Rkd.Scalar.Extensions;
using Rkd.Scalar.Security.Contracts;
using Rkd.Scalar.Tests.Helpers;
using FluentAssertions;

namespace Rkd.Scalar.Tests.Integration;

public class DependencyInjectionTests
{
    [Fact]
    public void WithUiProtection_ShouldEnableOptions()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        var scalar = services
            .AddRkdScalar(config)
            .WithUiProtection<FakeCredentialValidator>();

        var provider = services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<IOptions<ScalarUiProtectionOptions>>();

        options.Value.Enabled.Should().BeTrue();

        provider
            .GetService<IUiCredentialValidator>()
            .Should()
            .NotBeNull();
    }
}