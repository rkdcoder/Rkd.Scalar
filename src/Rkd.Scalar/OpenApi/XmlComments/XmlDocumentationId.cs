using System.Reflection;
using System.Text.RegularExpressions;

namespace Rkd.Scalar.OpenApi.XmlComments
{
    /// <summary>
    /// Builds XML documentation member ids (<c>T:</c>, <c>M:</c>, <c>P:</c>, <c>F:</c>) from reflection members,
    /// following the C# compiler format.
    /// </summary>
    internal static partial class XmlDocumentationId
    {
        public static string? For(MemberInfo member) => member switch
        {
            Type type => "T:" + DeclarationName(type),
            MethodInfo method => ForMethod(method),
            PropertyInfo property when property.DeclaringType is not null =>
                "P:" + DeclarationName(property.DeclaringType) + "." + property.Name + Parameters(property.GetIndexParameters()),
            FieldInfo field when field.DeclaringType is not null =>
                "F:" + DeclarationName(field.DeclaringType) + "." + field.Name,
            _ => null
        };

        private static string? ForMethod(MethodInfo method)
        {
            if (method.DeclaringType is null)
                return null;

            if (method.IsGenericMethod && !method.IsGenericMethodDefinition)
                method = method.GetGenericMethodDefinition();

            var declaringType = method.DeclaringType!;

            // Methods of a closed generic type must be documented against the open definition.
            if (declaringType.IsConstructedGenericType)
            {
                var definition = declaringType.GetGenericTypeDefinition();
                method = definition.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(m => m.MetadataToken == method.MetadataToken) ?? method;
                declaringType = definition;
            }

            var id = "M:" + DeclarationName(declaringType) + "." + method.Name.Replace('.', '#');

            if (method.IsGenericMethod)
                id += "``" + method.GetGenericArguments().Length;

            return id + Parameters(method.GetParameters());
        }

        private static string Parameters(ParameterInfo[] parameters) =>
            parameters.Length == 0
                ? string.Empty
                : "(" + string.Join(",", parameters.Select(p => ParameterTypeName(p.ParameterType))) + ")";

        private static string DeclarationName(Type type)
        {
            if (type.IsConstructedGenericType)
                type = type.GetGenericTypeDefinition();

            return (type.FullName ?? type.Name).Replace('+', '.');
        }

        private static string ParameterTypeName(Type type)
        {
            if (type.IsByRef)
                return ParameterTypeName(type.GetElementType()!) + "@";

            if (type.IsPointer)
                return ParameterTypeName(type.GetElementType()!) + "*";

            if (type.IsArray)
            {
                var rank = type.GetArrayRank();
                var suffix = rank == 1 ? "[]" : "[" + string.Join(",", Enumerable.Repeat("0:", rank)) + "]";
                return ParameterTypeName(type.GetElementType()!) + suffix;
            }

            if (type.IsGenericParameter)
                return (type.DeclaringMethod is null ? "`" : "``") + type.GenericParameterPosition;

            if (type.IsConstructedGenericType)
            {
                var definition = DeclarationName(type.GetGenericTypeDefinition());
                var arguments = type.GetGenericArguments().Select(ParameterTypeName);
                return ArityRegex().Replace(definition, string.Empty) + "{" + string.Join(",", arguments) + "}";
            }

            return (type.FullName ?? type.Name).Replace('+', '.');
        }

        [GeneratedRegex(@"`\d+")]
        private static partial Regex ArityRegex();
    }
}
