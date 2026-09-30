using FluentAssertions;

namespace Rkd.Scalar.Tests.Unit
{
    /// <summary>The server (Rkd.Scalar) and the clients (Rkd.Problems) derive the same codes and member names.</summary>
    public class SharedContractTests
    {
        [Fact]
        public void DefaultCodes_ShouldMatchRkdProblems()
        {
            for (var status = 100; status <= 599; status++)
                Errors.ProblemCodes.FromStatus(status).Should().Be(Rkd.Problems.ProblemCodes.FromStatus(status), $"status {status}");

            Errors.ProblemCodes.Validation.Should().Be(Rkd.Problems.ProblemCodes.Validation);
            Errors.ProblemCodes.ExtensionName.Should().Be(Rkd.Problems.ProblemMembers.Code);
        }
    }
}
