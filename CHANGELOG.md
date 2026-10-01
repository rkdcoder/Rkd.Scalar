# Changelog

All notable changes to this project are documented here.
The project follows [Semantic Versioning](https://semver.org/): public APIs are never
removed or changed in a breaking way within a major version (enforced by package validation).

## 2.8.1

### Changed

- Dependencies updated: Asp.Versioning 10.2.x, Scalar.AspNetCore 2.17.12, ASP.NET Core / Extensions 10.0.12 and
  Microsoft.OpenApi 2.12.2 (3.x is incompatible with Microsoft.AspNetCore.OpenApi 10.x). No public API change.
- README: "Reading errors in clients (Rkd.Problems)" — how Blazor, WPF, console/MCP and other backends read the
  errors with Rkd.Problems.

## 2.8.0

### Fixed

- The OpenAPI documents and the Scalar UI allow anonymous access: with a `FallbackPolicy` requiring authenticated
  users, `/scalar` and `/openapi/{documentName}.json` answered 401. Access stays governed by `RkdScalar:Enabled` and
  `WithUiProtection`.

### Added

- `RkdPem.ImportPrivateKey` / `ImportECDsaPrivateKey` / `ImportRsaPrivateKey`: the IIS-safe PEM import of 2.7.0 for
  keys managed outside `IJwtTokenService`.
- New package **Rkd.Scalar.FluentValidation**: `WithFluentValidation(assemblies)` registers the validators and
  validates every controller action argument; `.WithFluentValidation()` on minimal API endpoints and groups. Invalid
  requests answer with the Rkd.Scalar validation problem, field names following the JSON naming policy.

## 2.7.0

### Added

- `JwtOptions.AdditionalAudiences` (bindable from the `Jwt` section): issued tokens carry
  `"aud": [Audience, ...AdditionalAudiences]` and validation accepts all of them.
- `RkdJwtTokenOptions.ForwardIncomingToken`: `AddRkdJwtToken` forwards the `Authorization: Bearer` of the current
  request (the logged user's token) and issues a service token only when there is no request or no Bearer token.
- `RkdProblemDetailsOptions.MapUpstreamProblems()`: an `HttpProblemException` (Rkd.Problems) becomes the same 4xx
  (status, `code`, `title`, `detail`, `errors`) or a `502` for 5xx; the other API's status, code and `traceId` go to
  the log.
- Rkd.Scalar references **Rkd.Problems**: the members (`code`, `traceId`, `errors`), `VALIDATION_ERROR`, the default
  codes and the media type come from its shared constants (identical values).

### Fixed

- PEM private keys (`PrivateKeyPem` / `PrivateKeyPath`: PKCS#8, PKCS#1 and SEC1 on P-256/P-384/P-521) are decoded in
  managed code and imported with `ImportParameters`, so they load on IIS application pools without
  "Load User Profile", where `ImportFromPem` failed with `CryptographicException: The system cannot find the file
  specified`. Other formats keep using `ImportFromPem`.

## 2.6.0

### Added

- `[SensitiveHttpLog]` (controllers and actions), `.WithSensitiveHttpLog()` (minimal API endpoints and groups) and
  `ISensitiveHttpLogMetadata`: the HTTP log keeps the entry without its request and response bodies, for routes a
  `SensitivePaths` prefix cannot describe (e.g. `POST api/v1/systems/{id}/keys`).
- `RkdProblemDetailsOptions.UnexpectedError(code, detail, title)`: custom `code`, `detail` and `title` of the `500`
  written for unexpected exceptions, without `Map<Exception>`; the exception is still logged as `Error` and
  `IncludeExceptionDetails` still adds its details in Development.

## 2.5.1

### Fixed

- With `WithJsonNaming` (snake_case, kebab-case…) and `WithProblemDetails()`, problem details written by ASP.NET
  Core's default writer — unknown routes (404), 405, `Results.Problem`, `RkdResults` and exceptions in minimal
  APIs — carried the trace id twice (`trace_id` and `traceId`). The member is now always `traceId`, as documented.

## 2.5.0

### Added

- **HTTP logging** with `WithHttpLogging(options)`: every request captured by the outermost middleware (status,
  duration, route pattern, user, headers, request/response bodies, problem `code`, unhandled exception, trace id),
  queued in a bounded in-memory queue (never blocks, drops when full) and written in batches by a background service
  to the registered `IHttpLogSink`s (`WithHttpLogSink<T>()`), isolated from each other; queued entries are written on
  shutdown. Bodies are captured as they are read/written (no extra buffering, streaming preserved, text only, up to
  `MaxBodyBytes`); credentials are redacted (Authorization scheme kept, cookies, API key header, token query
  parameters); sensitive paths and the login endpoint never store bodies; Scalar/OpenAPI routes are excluded.
  Configurable user claims (`UserNameClaimTypes`, `UserIdClaimTypes`), `Filter`, `Enrich` and the `HttpLogging`
  configuration section.
- New package **Rkd.Scalar.HttpLogging.SqlServer**: `WriteHttpLogsToSqlServer(...)` writes each batch with
  `SqlBulkCopy`, optionally creates the schema/table (`CreateTable`, or `SqlServerHttpLogTable.CreateScript`), writes
  only the columns the table has and cuts oversized values.

## 2.4.0

### Added

- `ConfigureJwtBearer(Action<JwtBearerOptions>)`: events (`OnTokenValidated`, `OnMessageReceived`…) and any
  `JwtBearerOptions` setting without registering `AddJwtBearer` manually; applied after Rkd.Scalar's settings
  regardless of the call order.
- `IJwtSigningKeyResolver` + `WithJwtSigningKeyResolver<T>()`: validation keys resolved asynchronously by `kid`
  (database, key registry), cached (`KeyCacheDuration`), looked up again for unknown `kid`s (key rotation, throttled
  by `UnknownKeyCacheDuration`) and optionally bound to their issuer (`JwtSigningKey.Issuer`). `Jwt:Issuer` becomes
  optional when every resolved key has an issuer.
- Authentication failures with a `ProblemException` (`context.Fail(RkdError.Unauthorized("CODE", "…"))`) put its
  `code` and `detail` in the 401 problem details.
- `IProblemLogDetails` and `ProblemException.LogDetails` / `WithLogDetails(...)`: technical details written only to
  the log. 4xx log entries include the exception when it has an `InnerException`.
- `RkdProblemDetailsOptions.MapStatus(status, code, detail, title)` and `MapStatus(status, factory)` for body-less
  error responses (404, 405, 415, 401/403…).
- Exceeded form limits (`MultipartBodyLengthLimit`…) return `413` with `code: PAYLOAD_TOO_LARGE` (controllers,
  `ReadFormAsync`, minimal API binding in Development) instead of `500` / a validation error; malformed forms `400`.
- `IFormFile`, `IFormFileCollection` and `List<IFormFile>` are documented as `format: binary` (file picker in
  Scalar), and `[FromForm]` models with files as `multipart/form-data`.
- `IHttpClientBuilder.AddRkdJwtToken(identity)`: outgoing calls authenticated with a cached token from
  `IJwtTokenService` (renewed before expiration, works with async signers).
- `RkdScalarOptions.DarkMode` (`"RkdScalar": { "DarkMode": true }`).
- `[ApiModule]` accepts route parameters (`[ApiModule("tools/{toolId}")]`), left out of the default tag.

### Changed

- `BadHttpRequestException` 413 responses get a generic `detail` outside Development.

## 2.3.0

### Added

- **Error codes**: every problem details response has a machine-readable `code` — the one you set,
  `VALIDATION_ERROR` for validation problems or the status reason phrase (`NOT_FOUND`, `UNAUTHORIZED`,
  `INTERNAL_SERVER_ERROR`…). `IncludeDefaultCodes = false` keeps only explicit codes.
- `RkdError`: `throw RkdError.Conflict("CUSTOMER_ALREADY_EXISTS", "…")` — `BadRequest`, `Unauthorized`,
  `Forbidden`, `NotFound`, `Conflict`, `UnprocessableEntity`, `TooManyRequests`, `Create(status, code)` and
  `Validation(errors)` (same shape as ASP.NET Core validation, for FluentValidation/RuleWeaver/custom rules).
- `RkdResults` / `RkdProblemResult`: return the same problems from controllers (`IActionResult`,
  `ActionResult<T>`) and minimal APIs (`IResult`, `Results<…>`), with or without `WithProblemDetails()`.
- `ProblemException.Code` and `ProblemException.ToProblemDetails()` (reuse in `options.Map<T>` factories).
- **OpenAPI error responses** (`DocumentErrorResponses`, on by default): 400 for operations with input,
  401/403 for protected operations, 429 for rate limited operations and 500 for all, with the
  `application/problem+json` schema documenting `code` and `traceId`; declared error responses
  (`[ProducesResponseType(404)]`…) are moved to `application/problem+json`.

### Changed

- Outside Development, controller JSON conversion errors no longer expose .NET type names
  (`AllowInputFormatterExceptionMessages` follows `IncludeExceptionDetails`), and malformed request details
  are generic.

## 2.2.0

### Added

- `WithJsonNaming(JsonNamingPolicy)`: one call applies the naming policy to controllers (MVC `JsonOptions`),
  minimal APIs and the OpenAPI schemas shown by Scalar (`Http.Json.JsonOptions`), dictionary keys and model
  validation error names.
- `ConfigureJson(Action<JsonSerializerOptions>)`: any other serializer setting (converters, ignore conditions…)
  applied to both JSON settings at once.

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
