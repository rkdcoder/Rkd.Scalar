using System.Security.Claims;

namespace Rkd.Scalar.Security.Jwt
{
    /// <summary>
    /// Maps between the .NET <see cref="ClaimTypes"/> URIs and the standard JWT / OpenID Connect claim names.
    /// </summary>
    internal static class JwtClaimNames
    {
        public static readonly IReadOnlyDictionary<string, string> Outbound = new Dictionary<string, string>
        {
            [ClaimTypes.NameIdentifier] = "sub",
            [ClaimTypes.Name] = "name",
            [ClaimTypes.Role] = "role",
            [ClaimTypes.Email] = "email",
            [ClaimTypes.GivenName] = "given_name",
            [ClaimTypes.Surname] = "family_name",
            [ClaimTypes.MobilePhone] = "phone_number",
            [ClaimTypes.DateOfBirth] = "birthdate",
            [ClaimTypes.Gender] = "gender",
            [ClaimTypes.Webpage] = "website",
            [ClaimTypes.Locality] = "locale"
        };

        public static readonly IReadOnlyDictionary<string, string> Inbound =
            Outbound.ToDictionary(pair => pair.Value, pair => pair.Key);

        /// <summary>Registered claims written by the token service; identity claims never override them.</summary>
        public static readonly IReadOnlySet<string> Reserved =
            new HashSet<string>(StringComparer.Ordinal) { "iss", "aud", "exp", "nbf", "iat" };
    }
}
