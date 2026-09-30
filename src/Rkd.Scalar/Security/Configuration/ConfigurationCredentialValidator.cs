using Microsoft.Extensions.Configuration;
using Rkd.Scalar.Security.Basic;
using Rkd.Scalar.Security.Contracts;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Rkd.Scalar.Security.Configuration
{
    /// <summary>
    /// Validates Basic credentials against a configuration section or a fixed user list.
    /// </summary>
    /// <remarks>
    /// Supported configuration formats:
    /// <code>
    /// "Section": { "Username": "admin", "Password": "secret" }
    /// "Section": { "alice": "password1", "bob": "password2" }
    /// </code>
    /// Usernames are case-insensitive; passwords are compared in constant time.
    /// The section is read on every validation, so reloadable configuration sources are honored.
    /// </remarks>
    internal abstract class ConfigurationCredentialValidator : ICredentialValidator<BasicAuthCredentials>
    {
        private readonly Func<IReadOnlyDictionary<string, string>> _credentials;

        private readonly string _authenticationType;

        protected ConfigurationCredentialValidator(
            Func<IReadOnlyDictionary<string, string>> credentials,
            string authenticationType)
        {
            _credentials = credentials;
            _authenticationType = authenticationType;
        }

        public Task<ClaimsIdentity?> ValidateAsync(
            BasicAuthCredentials request,
            CancellationToken cancellationToken = default)
        {
            if (request is null ||
                string.IsNullOrWhiteSpace(request.Username) ||
                request.Password is null)
                return Task.FromResult<ClaimsIdentity?>(null);

            var valid =
                _credentials().TryGetValue(request.Username, out var expected) &&
                FixedTimeEquals(expected, request.Password);

            if (!valid)
                return Task.FromResult<ClaimsIdentity?>(null);

            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, request.Username) },
                _authenticationType);

            return Task.FromResult<ClaimsIdentity?>(identity);
        }

        internal static IReadOnlyDictionary<string, string> ReadSection(IConfigurationSection section)
        {
            var credentials = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var singleUsername = section["Username"];
            var singlePassword = section["Password"];

            if (!string.IsNullOrWhiteSpace(singleUsername) && !string.IsNullOrEmpty(singlePassword))
                credentials[singleUsername] = singlePassword;

            foreach (var child in section.GetChildren())
            {
                if (child.Key.Equals("Username", StringComparison.OrdinalIgnoreCase) ||
                    child.Key.Equals("Password", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(child.Key) && !string.IsNullOrEmpty(child.Value))
                    credentials[child.Key] = child.Value;
            }

            return credentials;
        }

        internal static Func<IReadOnlyDictionary<string, string>> FromSection(
            IConfiguration configuration,
            string sectionName)
        {
            var section = configuration.GetSection(sectionName);

            if (ReadSection(section).Count == 0)
                throw new InvalidOperationException(
                    $"Configuration section '{sectionName}' was not found or contains no credentials. " +
                    $"Expected \"{sectionName}\": {{ \"Username\": \"...\", \"Password\": \"...\" }} " +
                    $"or \"{sectionName}\": {{ \"user1\": \"password1\" }}.");

            return () => ReadSection(section);
        }

        internal static Func<IReadOnlyDictionary<string, string>> FromUser(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Username cannot be empty.", nameof(username));

            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password cannot be empty.", nameof(password));

            IReadOnlyDictionary<string, string> credentials =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [username] = password };

            return () => credentials;
        }

        internal static bool FixedTimeEquals(string expected, string actual) =>
            CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
                SHA256.HashData(Encoding.UTF8.GetBytes(actual)));
    }

    /// <summary>Configuration-based validator used by <c>WithUiProtection(sectionName)</c>.</summary>
    internal sealed class UiConfigurationCredentialValidator
        : ConfigurationCredentialValidator, IUiCredentialValidator
    {
        public UiConfigurationCredentialValidator(Func<IReadOnlyDictionary<string, string>> credentials)
            : base(credentials, "Basic")
        {
        }
    }

    /// <summary>Configuration-based validator used by <c>WithBasicAuth(sectionName)</c>.</summary>
    internal sealed class BasicConfigurationCredentialValidator : ConfigurationCredentialValidator
    {
        public BasicConfigurationCredentialValidator(Func<IReadOnlyDictionary<string, string>> credentials)
            : base(credentials, "Basic")
        {
        }
    }
}
