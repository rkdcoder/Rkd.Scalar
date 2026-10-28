using System.Xml.Linq;
using FluentAssertions;

namespace Rkd.Scalar.Tests.Unit
{
    /// <summary>
    /// The contracts other layers reference (Rkd.Scalar.Abstractions and Rkd.Scalar.Errors) stay light and keep
    /// working for code compiled against Rkd.Scalar 2.9 and earlier.
    /// </summary>
    public class PackageSplitTests
    {
        private static readonly string[] HeavyDependencies =
            ["Rkd.Scalar", "Scalar.AspNetCore", "Asp.Versioning", "Microsoft.OpenApi", "Microsoft.AspNetCore.OpenApi", "Microsoft.AspNetCore.Authentication"];

        [Theory]
        [InlineData("Rkd.Scalar.ApiKeyCredentials", "Rkd.Scalar.Abstractions")]
        [InlineData("Rkd.Scalar.BasicAuthCredentials", "Rkd.Scalar.Abstractions")]
        [InlineData("Rkd.Scalar.CredentialValidationResult", "Rkd.Scalar.Abstractions")]
        [InlineData("Rkd.Scalar.ICredentialValidator`1", "Rkd.Scalar.Abstractions")]
        [InlineData("Rkd.Scalar.RkdScalarAuthenticationSchemes", "Rkd.Scalar.Abstractions")]
        [InlineData("Rkd.Scalar.IJwtSigner", "Rkd.Scalar.Abstractions")]
        [InlineData("Rkd.Scalar.IJwtSigningKeyResolver", "Rkd.Scalar.Abstractions")]
        [InlineData("Rkd.Scalar.IJwtTokenService", "Rkd.Scalar.Abstractions")]
        [InlineData("Rkd.Scalar.JwtLoginResponse", "Rkd.Scalar.Abstractions")]
        [InlineData("Rkd.Scalar.JwtSigningKey", "Rkd.Scalar.Abstractions")]
        [InlineData("Rkd.Scalar.JwtSigningKeyContext", "Rkd.Scalar.Abstractions")]
        [InlineData("Rkd.Scalar.JwtToken", "Rkd.Scalar.Abstractions")]
        [InlineData("Rkd.Scalar.RkdPem", "Rkd.Scalar.Abstractions")]
        [InlineData("Rkd.Scalar.HttpLogEntry", "Rkd.Scalar.Abstractions")]
        [InlineData("Rkd.Scalar.IHttpLogSink", "Rkd.Scalar.Abstractions")]
        [InlineData("Rkd.Scalar.IProblemLogDetails", "Rkd.Scalar.Errors")]
        [InlineData("Rkd.Scalar.ProblemException", "Rkd.Scalar.Errors")]
        [InlineData("Rkd.Scalar.RkdError", "Rkd.Scalar.Errors")]
        public void MovedTypes_ShouldStillResolveFromRkdScalar(string typeName, string assemblyName)
        {
            // How a binary compiled against 2.9 refers to the type: through the Rkd.Scalar assembly.
            var type = Type.GetType($"{typeName}, Rkd.Scalar", throwOnError: true)!;

            type.Assembly.GetName().Name.Should().Be(assemblyName);
        }

        [Fact]
        public void Abstractions_ShouldNotDependOnAspNetCore()
        {
            var references = typeof(ICredentialValidator<>).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

            references.Should().NotContain(name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
            references.Should().NotContain(name => HeavyDependencies.Any(heavy => name.StartsWith(heavy, StringComparison.Ordinal)));
        }

        [Fact]
        public void Errors_ShouldOnlyDependOnRkdProblemsAndTheFramework()
        {
            var references = typeof(RkdError).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

            references.Should().NotContain(name => HeavyDependencies.Any(heavy => name.StartsWith(heavy, StringComparison.Ordinal)));
            references.Should().NotContain(name => name.StartsWith("Microsoft.IdentityModel", StringComparison.Ordinal));
        }

        [Fact]
        public void XmlDocumentation_ShouldNotDescribeAMemberInTwoPackages()
        {
            // The OpenAPI XML comment generator of the applications reads the documentation of every referenced
            // assembly into one dictionary: a member documented by two packages makes every OpenAPI document fail.
            // The helpers that generator adds to each library (Microsoft.AspNetCore.OpenApi.Generated) are not ours.
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            var duplicates = new List<string>();

            foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "Rkd.Scalar*.xml").Where(f => !f.EndsWith("Rkd.Scalar.Tests.xml", StringComparison.Ordinal)))
            {
                var members = XDocument.Load(file).Descendants("member")
                    .Select(m => (string)m.Attribute("name")!)
                    .Where(name => !name.Contains("Microsoft.AspNetCore.OpenApi.Generated.", StringComparison.Ordinal));

                foreach (var member in members)
                {
                    if (!owners.TryAdd(member, Path.GetFileName(file)))
                        duplicates.Add($"{member} ({owners[member]} and {Path.GetFileName(file)})");
                }
            }

            owners.Should().ContainKey("T:Rkd.Scalar.ICredentialValidator`1", "the documentation files are copied to the test output");
            duplicates.Should().BeEmpty();
        }

        [Theory]
        [InlineData("CUSTOMER_NOT_FOUND")]
        [InlineData("orders.item-missing_2")]
        [InlineData("PEDIDO_NÃO_ENCONTRADO")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("NOT FOUND")]
        [InlineData("orders/missing")]
        [InlineData("<script>")]
        public void ErrorCodeRules_ShouldBeTheSameInRkdScalarAndRkdScalarErrors(string code)
        {
            var scalar = Record.Exception(() => Errors.ProblemCodes.Validate(code));
            var errors = Record.Exception(() => new ProblemException(400) { Code = code });

            errors?.GetType().Should().Be(scalar?.GetType());
            errors?.Message.Should().Be(scalar?.Message);
        }
    }
}
