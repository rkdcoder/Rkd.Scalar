# Migrating from 1.x to 2.0

Rkd.Scalar 2.0 cleans up the public API: a single namespace, clearer names, a validation result
with a failure reason and standard JWT claims. Most applications migrate in a few minutes.

## 1. Namespaces

Everything you use lives in `Rkd.Scalar`, and the entry points moved to the standard ASP.NET Core
namespaces, so `Program.cs` usually needs no `using` at all.

```diff
- using Rkd.Scalar.Extensions;
- using Rkd.Scalar.Builder;
- using Rkd.Scalar.Configuration;
- using Rkd.Scalar.Security.Contracts;
- using Rkd.Scalar.Security.Basic;
- using Rkd.Scalar.Security.ApiKey;
- using Rkd.Scalar.Security.Jwt;
- using Rkd.Scalar.Security;
+ using Rkd.Scalar;
```

| 1.x                                                 | 2.0                                                        |
| --------------------------------------------------- | ---------------------------------------------------------- |
| `ScalarBuilder`                                     | `RkdScalarBuilder`                                         |
| `RkdScalarConfiguration`                            | `RkdScalarOptions`                                         |
| `RkdScalarExtensions.AddRkdScalar/UseRkdScalar`     | same methods, in `Microsoft.Extensions.*` / `Microsoft.AspNetCore.Builder` |

## 2. `UseRkdScalar`

```diff
- app.UseRkdScalar(new RkdScalarConfiguration { Title = "My API" });
+ app.UseRkdScalar(o => o.Title = "My API");
```

`UseRkdScalar()` still reads the `RkdScalar` section; `UseRkdScalar("Section", o => ...)` reads another one.
The default title changed from `API Documentation.` to `API Documentation`.

## 3. Validators return `CredentialValidationResult`

```diff
- public Task<ClaimsIdentity?> ValidateAsync(LoginRequest request, CancellationToken ct = default)
+ public Task<CredentialValidationResult> ValidateAsync(LoginRequest request, CancellationToken ct = default)
  {
-     if (!valid) return Task.FromResult<ClaimsIdentity?>(null);
-     return Task.FromResult<ClaimsIdentity?>(identity);
+     if (!valid) return Task.FromResult(CredentialValidationResult.Failure("Invalid credentials."));
+     return Task.FromResult(CredentialValidationResult.Success(identity));
  }
```

In `async` validators a `ClaimsIdentity` converts implicitly, so `return identity;` keeps compiling
(`null` means failure). The failure reason is returned in the login endpoint's 401 response
(`application/problem+json`, field `detail`) and logged by the Basic / API Key handlers.

The interface's type parameter was renamed from `TRequest` to `TCredentials` (no code change needed).

## 4. JWT settings

The default configuration section is now **`Jwt`** (was `JwtOptions`), and `JwtSettings` was merged
into `JwtOptions`, which binds directly from configuration.

```diff
  {
-   "JwtOptions": {
+   "Jwt": {
      "Secret": "...",
      "Issuer": "my-api",
      "Audience": "my-clients",
-     "Expiration": 2,
-     "ExpirationMinutes": 30,
-     "ValidateNotBefore": true
+     "ExpirationInMinutes": 120
    }
  }
```

To keep the old section name: `.WithBearerAuth("JwtOptions")`.

| 1.x (`JwtOptions` / `JwtSettings`)          | 2.0 (`JwtOptions`)                                   |
| ------------------------------------------- | ---------------------------------------------------- |
| `Expiration` (`TimeSpan` / hours)           | `ExpirationInMinutes` (default `60`)                 |
| `ExpirationMinutes`                         | `ExpirationInMinutes`                                |
| `ClockSkew` / `ClockSkewSeconds` (default 0)| `ClockSkewInSeconds` (default **30**)                |
| `ValidateNotBefore`                         | removed — `nbf` is always written                    |
| —                                           | `UseStandardClaimNames` (default `true`)             |

`Issuer` (unless `Authority` is used) and `Audience` are now required and checked at startup.

## 5. JWT claims (tokens change)

Tokens now use the standard JWT claim names and always carry `iat`, `nbf` and `jti`:

```diff
- "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name": "rodrigo"
- "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": "admin"
+ "name": "rodrigo",
+ "role": "admin",
+ "sub": "42", "iat": 1790000000, "jti": "5f0c…"
```

- **APIs validated by Rkd.Scalar**: nothing changes — the claims are mapped back to `ClaimTypes.*`,
  so `User.Identity.Name`, `IsInRole`, `[Authorize(Roles = ...)]` and
  `FindFirst(ClaimTypes.NameIdentifier)` keep working.
- **Other consumers** (another stack, a gateway, a front end reading the token): read `sub`,
  `name`, `role`… instead of the URIs, or set `"UseStandardClaimNames": false` to keep the 1.x format.
- Tokens issued by 1.x remain valid until they expire.

## 6. Token service

```diff
- JwtTokenResult token = jwtService.GenerateToken(identity);
- JwtTokenResult token = await jwtService.GenerateTokenAsync(identity);
- return Ok(JwtLoginResponse.FromResult(token));
+ JwtToken token = await jwtService.CreateTokenAsync(identity, cancellationToken: ct);
+ return Ok(JwtLoginResponse.FromToken(token));
```

| 1.x `JwtTokenResult`       | 2.0 `JwtToken`                         |
| -------------------------- | -------------------------------------- |
| `Token`                    | `AccessToken`                          |
| `ExpiresAtUtc` (`DateTime`)| `ExpiresAt` (`DateTimeOffset`)         |
| —                          | `TokenId` (`jti`), `IssuedAt`          |

`JwtTokenService` is no longer public: inject `IJwtTokenService`. Custom implementations implement
the single `CreateTokenAsync` method.

## 7. Login endpoint

```diff
- .WithDefaultJwtLogin<LoginRequest>("/auth/login", 5, TimeSpan.FromMinutes(1))
+ .WithJwtLoginEndpoint<LoginRequest>()                        // same defaults
+ .WithJwtLoginEndpoint<LoginRequest>("/auth/login", 5, TimeSpan.FromMinutes(1))
```

The 401 response is now `application/problem+json` with the validator's failure reason.
`expires_at` is serialized with its UTC offset (`2026-01-01T12:00:00+00:00`).

## 8. API Key

```diff
- .WithApiKeyAuth<ApiKeyValidator>()
+ .WithApiKeyAuth<ApiKeyValidator>()                                  // unchanged
- .WithApiKeyAuth<ApiKeyValidator>(configure)
+ .WithApiKeyAuth<ApiKeyValidator>(o => o.HeaderName = "X-Partner-Key")  // now an optional parameter
```

## 9. Removed from the public API

These types were implementation details and are now internal or removed:
`BasicAuthParser`, `ScalarUiAuthMiddleware`, `ScalarUiProtectionOptions`, `IUiCredentialValidator`,
`JwtTokenService`, `JwtSettings` (merged into `JwtOptions`), `JwtLoginRequest` (unused) and
`JwtTokenResult` (replaced by `JwtToken`).

## Checklist

1. Replace the `using Rkd.Scalar.*` lines with `using Rkd.Scalar;`.
2. Change validators to return `CredentialValidationResult`.
3. Rename the `JwtOptions` section to `Jwt` and `Expiration`/`ExpirationMinutes` to `ExpirationInMinutes`.
4. Replace `GenerateToken`/`GenerateTokenAsync` with `CreateTokenAsync` and `FromResult` with `FromToken`.
5. Replace `WithDefaultJwtLogin` with `WithJwtLoginEndpoint`.
6. If other systems read your tokens, switch them to the standard claim names (or set `UseStandardClaimNames` to `false`).
