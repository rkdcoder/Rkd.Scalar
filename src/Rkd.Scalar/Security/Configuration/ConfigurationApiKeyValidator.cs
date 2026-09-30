using Microsoft.Extensions.Configuration;
using Rkd.Scalar.Security.Contracts;
using System.Security.Claims;

namespace Rkd.Scalar.Security.Configuration
{
    /// <summary>
    /// Validates API keys against a configuration section.
    /// </summary>
    /// <remarks>
    /// Supported configuration formats:
    /// <code>
    /// "ApiKeys": { "partner-a": "key-1", "partner-b": "key-2" }
    /// "ApiKeys": [ "key-1", "key-2" ]
    /// "ApiKeys": { "partner-a": { "Key": "key-1", "Roles": [ "reader" ] } }
    /// </code>
    /// The client name becomes the <see cref="ClaimTypes.Name"/> claim; keys are compared in constant time.
    /// </remarks>
    internal sealed class ConfigurationApiKeyValidator : ICredentialValidator<ApiKeyCredentials>
    {
        private const string DefaultClientName = "ApiKeyClient";

        private readonly IConfigurationSection _section;

        public ConfigurationApiKeyValidator(IConfiguration configuration, string sectionName)
        {
            _section = configuration.GetSection(sectionName);

            if (ReadClients(_section).Count == 0)
                throw new InvalidOperationException(
                    $"Configuration section '{sectionName}' was not found or contains no API keys. " +
                    $"Expected \"{sectionName}\": {{ \"client-name\": \"api-key\" }} or \"{sectionName}\": [ \"api-key\" ].");
        }

        public Task<CredentialValidationResult> ValidateAsync(
            ApiKeyCredentials request,
            CancellationToken cancellationToken = default)
        {
            if (request is null || string.IsNullOrEmpty(request.Key))
                return Task.FromResult(CredentialValidationResult.Failure());

            ApiKeyClient? match = null;

            // Compare against every key so the response time does not reveal which one matched.
            foreach (var client in ReadClients(_section))
            {
                if (ConfigurationCredentialValidator.FixedTimeEquals(client.Key, request.Key))
                    match ??= client;
            }

            if (match is null)
                return Task.FromResult(CredentialValidationResult.Failure());

            var claims = new List<Claim> { new(ClaimTypes.Name, match.Name) };
            claims.AddRange(match.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

            return Task.FromResult(CredentialValidationResult.Success(
                new ClaimsIdentity(claims, RkdScalarAuthenticationSchemes.ApiKey)));
        }

        private static List<ApiKeyClient> ReadClients(IConfigurationSection section)
        {
            var clients = new List<ApiKeyClient>();

            foreach (var child in section.GetChildren())
            {
                var isIndex = int.TryParse(child.Key, out _);
                var name = isIndex ? DefaultClientName : child.Key;

                if (!string.IsNullOrEmpty(child.Value))
                {
                    clients.Add(new ApiKeyClient(name, child.Value, Array.Empty<string>()));
                    continue;
                }

                var key = child["Key"];

                if (string.IsNullOrEmpty(key))
                    continue;

                var roles = child.GetSection("Roles").GetChildren()
                    .Select(r => r.Value)
                    .Where(r => !string.IsNullOrWhiteSpace(r))
                    .Select(r => r!)
                    .ToArray();

                clients.Add(new ApiKeyClient(child["Name"] ?? name, key, roles));
            }

            return clients;
        }

        private sealed record ApiKeyClient(string Name, string Key, IReadOnlyList<string> Roles);
    }
}
