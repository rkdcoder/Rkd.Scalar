using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Rkd.Scalar.Security.Jwt
{
    /// <summary>
    /// Token handler of the Bearer scheme: maps the standard claim names back to <see cref="System.Security.Claims.ClaimTypes"/>
    /// and, when an <see cref="IJwtSigningKeyResolver"/> is registered, resolves the validation keys by <c>kid</c>
    /// asynchronously before validating (the synchronous <c>IssuerSigningKeyResolver</c> cannot query a database).
    /// </summary>
    internal sealed class RkdJsonWebTokenHandler : JsonWebTokenHandler
    {
        private readonly JwtSigningKeyCache? _keys;

        private readonly bool _requireBoundIssuer;

        public RkdJsonWebTokenHandler(JwtSigningKeyCache? keys, bool requireBoundIssuer)
        {
            _keys = keys;
            _requireBoundIssuer = requireBoundIssuer;

            MapInboundClaims = true;

            foreach (var (standard, dotnet) in JwtClaimNames.Inbound)
                InboundClaimTypeMap[standard] = dotnet;
        }

        public override async Task<TokenValidationResult> ValidateTokenAsync(string token, TokenValidationParameters validationParameters)
        {
            if (_keys is null || validationParameters is null || !CanReadToken(token))
                return await base.ValidateTokenAsync(token, validationParameters);

            JsonWebToken jwt;

            try
            {
                jwt = ReadJsonWebToken(token);
            }
            catch (Exception)
            {
                return await base.ValidateTokenAsync(token, validationParameters);
            }

            var issuer = jwt.TryGetPayloadValue<string>(JwtRegisteredClaimNames.Iss, out var iss) ? iss : null;
            var resolved = await _keys.GetKeysAsync(new JwtSigningKeyContext(jwt.Kid, issuer, jwt.Alg), CancellationToken.None);

            var parameters = validationParameters.Clone();
            var configured = new List<SecurityKey>();

            if (validationParameters.IssuerSigningKey is not null)
                configured.Add(validationParameters.IssuerSigningKey);

            if (validationParameters.IssuerSigningKeys is not null)
                configured.AddRange(validationParameters.IssuerSigningKeys);

            parameters.IssuerSigningKeys = configured.Concat(resolved).ToList();

            var resolvedKeys = new HashSet<SecurityKey>(resolved, ReferenceEqualityComparer.Instance);

            parameters.IssuerSigningKeyValidator = (key, securityToken, _) =>
            {
                if (!_keys.IsAllowed(key, securityToken.Issuer, _requireBoundIssuer, resolvedKeys.Contains(key)))
                    return false;

                // Keeps the original checks: your own IssuerSigningKeyValidator or the key lifetime validation.
                Validators.ValidateIssuerSecurityKey(key, securityToken, validationParameters);
                return true;
            };

            return await base.ValidateTokenAsync(token, parameters);
        }
    }
}
