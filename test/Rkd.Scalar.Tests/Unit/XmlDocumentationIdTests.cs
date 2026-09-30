using FluentAssertions;
using Rkd.Scalar.OpenApi.XmlComments;
using System.Xml.Linq;

namespace Rkd.Scalar.Tests.Unit
{
    public class XmlDocumentationIdTests
    {
        public class Outer
        {
            public class Inner
            {
                public string? Name { get; set; }
            }

            public void Simple() { }

            public void Complex(int? a, string[] b, List<Dictionary<string, Inner>> c, ref int d, CancellationToken e) { }

            public T Generic<T>(T value, IEnumerable<T> values) => value;
        }

        public class Box<T>
        {
            public void Put(T item) { }
        }

        [Theory]
        [InlineData(nameof(Outer.Simple), "M:Rkd.Scalar.Tests.Unit.XmlDocumentationIdTests.Outer.Simple")]
        [InlineData(nameof(Outer.Complex), "M:Rkd.Scalar.Tests.Unit.XmlDocumentationIdTests.Outer.Complex(System.Nullable{System.Int32},System.String[],System.Collections.Generic.List{System.Collections.Generic.Dictionary{System.String,Rkd.Scalar.Tests.Unit.XmlDocumentationIdTests.Outer.Inner}},System.Int32@,System.Threading.CancellationToken)")]
        [InlineData(nameof(Outer.Generic), "M:Rkd.Scalar.Tests.Unit.XmlDocumentationIdTests.Outer.Generic``1(``0,System.Collections.Generic.IEnumerable{``0})")]
        public void Methods_ShouldMatchCompilerIds(string name, string expected)
        {
            XmlDocumentationId.For(typeof(Outer).GetMethod(name)!).Should().Be(expected);
        }

        [Fact]
        public void TypesPropertiesAndGenericTypes_ShouldMatchCompilerIds()
        {
            XmlDocumentationId.For(typeof(Outer.Inner)).Should().Be("T:Rkd.Scalar.Tests.Unit.XmlDocumentationIdTests.Outer.Inner");
            XmlDocumentationId.For(typeof(Outer.Inner).GetProperty(nameof(Outer.Inner.Name))!)
                .Should().Be("P:Rkd.Scalar.Tests.Unit.XmlDocumentationIdTests.Outer.Inner.Name");
            XmlDocumentationId.For(typeof(Box<int>).GetMethod(nameof(Box<int>.Put))!)
                .Should().Be("M:Rkd.Scalar.Tests.Unit.XmlDocumentationIdTests.Box`1.Put(`0)");
        }

        [Fact]
        public void ToText_ShouldFlattenInlineTagsAndWhitespace()
        {
            var element = XElement.Parse("""
                <summary>
                    Returns <see cref="T:System.String"/> for <paramref name="id"/>,
                    or <see langword="null"/>. Uses <c>code</c>.
                    <para>Second paragraph.</para>
                </summary>
                """);

            XmlDocumentationProvider.ToText(element).Should()
                .Be("Returns String for id, or null. Uses code.\n\nSecond paragraph.");
        }
    }
}
