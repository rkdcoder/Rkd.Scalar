# Changelog

All notable changes to this project are documented here.
The project follows [Semantic Versioning](https://semver.org/): public APIs are never
removed or changed in a breaking way within a major version (enforced by package validation).

## 2.1.0

### Added

- **Standardized errors (RFC 9457)** with `WithProblemDetails(options)`:
  - unhandled exceptions become `500 application/problem+json` without messages or stack traces outside
    Development (`IncludeExceptionDetails` to override);
  - exception mapping by type (`options.Map<TException>(status, title, exposeMessage)` or a factory), most
    specific type wins, 5xx never exposes the message by default;
  - `ProblemException` (plus `NotFound`, `Conflict`, `BadRequest`, `Forbidden`, `UnprocessableEntity`
    shortcuts) to return a problem with custom type and extensions;
  - body-less error responses (unknown routes, 405, 415, 401/403 from authorization) become problems,
    with `[SkipStatusCodePages]` / `HandleStatusCodes` opt-outs;
  - every problem (including `Results.Problem` and model validation) gets `instance` and `traceId`;
    `Customize` adds your own members;
  - always JSON regardless of `Accept`, `Cache-Control: no-store` on exception responses, `499` without
    error logs for aborted requests, same behavior in Development (developer exception page filter);
  - the middleware is registered at the beginning of the pipeline automatically.
- **API modules**: `[ApiModule("billing")]` groups controllers under a standardized route
  (`api/v{version:apiVersion}/[module]/[controller]` with versioning, `api/[module]/[controller]` without),
  uses the module as OpenAPI/Scalar tag (customizable with `Tag`) and applies `[ApiController]`.
  Templates can be changed globally (`WithApiModules(o => o.RouteTemplate = ...)`) or per controller
  (`RouteTemplate = ...`), always with the `[module]` token.

## 2.0.0

Major version with breaking changes — see [MIGRATION.md](MIGRATION.md).

### Changed (breaking)

- **Single namespace**: every public type lives in `Rkd.Scalar`; `AddRkdScalar` / `UseRkdScalar` moved to
  `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Hosting` and `Microsoft.AspNetCore.Builder`.
- **Renames**: `ScalarBuilder` → `RkdScalarBuilder`, `RkdScalarConfiguration` → `RkdScalarOptions`,
  `WithDefaultJwtLogin` → `WithJwtLoginEndpoint` (with defaults), `JwtTokenResult` → `JwtToken`,
  `GenerateToken(Async)` → `CreateTokenAsync`, `JwtLoginResponse.FromResult` → `FromToken`.
- **Validators** return `CredentialValidationResult` (success with identity, or failure with a reason);
  `ClaimsIdentity` converts implicitly.
- **JWT options**: `JwtSettings` merged into `JwtOptions` (bindable directly); default section `Jwt`;
  `ExpirationInMinutes` (default 60) and `ClockSkewInSeconds` (default 30); `ValidateNotBefore` removed
  (`nbf` always written); `Issuer` / `Audience` validated at startup.
- **Standard JWT claims**: tokens use `sub`, `name`, `role`, `email`… and carry `iat`, `nbf` and `jti`;
  validation maps them back to `ClaimTypes.*`. `UseStandardClaimNames = false` keeps the 1.x format.
- **Login endpoint**: 401 responses are `application/problem+json` with the failure reason.
- `UseRkdScalar` takes `Action<RkdScalarOptions>` (and an optional section name) and returns `WebApplication`.
- Implementation details are no longer public: `BasicAuthParser`, `ScalarUiAuthMiddleware`,
  `ScalarUiProtectionOptions`, `IUiCredentialValidator`, `JwtTokenService`; `JwtLoginRequest` removed.
- Default Scalar title is `API Documentation` (no trailing dot).

### Added

- `JwtToken.TokenId`, `IssuedAt` and `ExpiresAt` (`DateTimeOffset`) for auditing and revocation.
- `CredentialValidationResult.Success(params IEnumerable<Claim>)` shortcut.
- `TimeProvider` support in the token service (registered `TimeProvider` is used when present).

## 1.3.0

### Added

- **Version selector always available**: the Scalar UI lists every API version in its dropdown,
  newest first, with deprecated versions flagged as `(deprecated)`. `/scalar` opens the newest
  non-deprecated version; `/scalar/v1` redirects to the dropdown with `v1` selected (existing links
  keep working). Disable with `RkdScalarConfiguration.VersionSelector = false`.
- Deprecation is read from `[ApiVersion(..., Deprecated = true)]` / `HasDeprecatedApiVersion` on the
  endpoints (Asp.Versioning 10 only reports it through deprecation policies) and from deprecation policies.

### Changed

- README and launch settings now point to `/scalar` instead of `/scalar/v1`.

## 1.2.0

### Added

- **XML comments applied automatically** to every OpenAPI document (all API versions):
  `<summary>`, `<remarks>`, `<param>`, `<returns>` and `<response code>` of controller actions and
  minimal API handlers, plus `<summary>` of models and their properties. The `.xml` documentation
  files are read at runtime, so `builder.Services.AddOpenApi("v1")` is no longer needed in the
  application (keeping it is harmless). Requires `GenerateDocumentationFile` in the API project.
- `WithXmlComments(bool)` to turn the feature off.
- `ConfigureOpenApi(Action<OpenApiOptions>)` to register your own transformers once for every document.

## 1.1.0

### Added

- **Asymmetric JWT**: `JwtOptions` accepts RSA / ECDSA keys (`PrivateKeyPem`, `PrivateKeyPath`,
  `PublicKeyPem`, `PublicKeyPath`), any `SecurityKey` (`SigningKey`, e.g. X509) and extra
  `ValidationKeys` for key rotation. Algorithm (`RS256`, `ES256`…) and `kid` are inferred.
- **OIDC / JWKS validation**: `JwtOptions.Authority` and `MetadataAddress` validate tokens issued by
  Keycloak, Entra ID, Auth0 or any OpenID Connect provider.
- **Async signing**: `IJwtSigner` + `WithJwtSigner<TSigner>()` delegate the signature to a KMS,
  Key Vault or HSM. `IJwtTokenService.GenerateTokenAsync` (default implementation provided, so
  existing implementations keep compiling).
- `WithJwtTokenService<TService>()` to replace the token issuer.
- `WithJwksEndpoint()` publishes the public keys at `/.well-known/jwks.json`.
- `JwtOptions.Algorithm`, `KeyId`, `ClockSkew`, `RequireHttpsMetadata`; `JwtSettings.ExpirationMinutes`,
  `ClockSkewSeconds` and every new key option bindable from appsettings.json.
- `JwtLoginResponse` (`access_token`, `token_type`, `expires_in`, `expires_at`) for your own login endpoints.
- Configuration-based validators, no code required: `WithUiProtection(sectionName)`,
  `WithUiProtection(username, password)`, `WithBasicAuth(sectionName)`, `WithApiKeyAuth(sectionName)`.
- `WithApiKeyAuth<TValidator>(configure)` and `ApiKeyAuthenticationOptions.HeaderName` (custom header).
- `WithDefaultAuthenticationScheme()`: a plain `[Authorize]` accepts Bearer, Basic and API Key.
- `WithOperationSecurity()`: security requirements only on operations that require authorization.
- `WithVersioning(configure, versions)` to customize `ApiVersioningOptions`.
- `WithDefaultJwtLogin<TCredential>(path, rateLimitPolicy)` to use your own rate limiting policy.
- `builder.AddRkdScalar()` (`IHostApplicationBuilder`), `app.UseRkdScalar()` and
  `app.UseRkdScalar(sectionName, configure)` reading the `RkdScalar` configuration section.
- `RkdScalarConfiguration.ScalarRoutePrefix` and `Enabled`.
- `RkdScalarAuthenticationSchemes` constants (`Bearer`, `Basic`, `ApiKey`, `All`).

### Fixed

- The README is now shipped in the package (`PackageReadmeFile`) and shows up in Visual Studio and nuget.org.
- XML documentation is shipped, so IntelliSense shows the API docs.
- A custom `OpenApiRoutePattern` is now passed to Scalar and honored by the UI protection
  (previously the UI kept requesting `/openapi/...` and protection only covered the default routes).
- The default login rate limiter is partitioned per client IP; previously a single client could
  exhaust the limit for everybody.
- The default login endpoint no longer overrides an `OnRejected` handler you configured, and
  sends the real `Retry-After`.
- Authentication handlers, the UI protection middleware and the login endpoint forward the request
  `CancellationToken` to the validators.

### Changed

- `JwtOptions` members are no longer `required` (source and binary compatible); defaults:
  `Expiration` = 1 hour, `ClockSkew` = zero.
- The HMAC signing key is created once instead of on every token.
- Package: SourceLink, deterministic CI builds, package validation against the last published
  version, CI runs on pull requests and skips already published versions.
