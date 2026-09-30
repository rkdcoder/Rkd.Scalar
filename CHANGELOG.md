# Changelog

All notable changes to this project are documented here.
The project follows [Semantic Versioning](https://semver.org/): public APIs are never
removed or changed in a breaking way within a major version (enforced by package validation).

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
