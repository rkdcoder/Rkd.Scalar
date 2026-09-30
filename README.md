# Rkd.Scalar - API Documentation Platform for ASP.NET

<p align="center">
  <img src="https://raw.githubusercontent.com/rkdcoder/Rkd.Scalar/master/src/Rkd.Scalar/Media/icon.png" width="128" alt="Rkd.Scalar logo" />
</p>

[![NuGet](https://img.shields.io/nuget/v/Rkd.Scalar.svg)](https://www.nuget.org/packages/Rkd.Scalar)
[![Build & Publish](https://github.com/rkdcoder/Rkd.Scalar/actions/workflows/main.yml/badge.svg)](https://github.com/rkdcoder/Rkd.Scalar/actions/workflows/main.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/rkdcoder/Rkd.Scalar/blob/master/LICENSE)

**Rkd.Scalar** adds a production-ready API documentation platform to ASP.NET Core.
It combines documentation, authentication helpers and security protections into a single fluent configuration layer.

> **Upgrading from 1.x?** See the [migration guide](https://github.com/rkdcoder/Rkd.Scalar/blob/master/MIGRATION.md).

---

# Core Features

- **Scalar UI** with a **version dropdown** (every API version, newest first, deprecated ones flagged)
- **OpenAPI generation** with **XML comments applied automatically** (every version, no extra code)
- **JWT Bearer authentication** — HMAC secret, **RSA / ECDSA keys**, **OIDC authority (JWKS)** or **async / KMS signing**,
  with **standard claim names** (`sub`, `name`, `role`…) and `iat` / `jti`
- **Standardized errors (RFC 9457)** — every exception and error response becomes `application/problem+json`,
  with exception mapping and safe defaults (no internals leaked in production)
- **API Key** and **Basic** authentication
- **Configuration-based validators** — protect the UI, Basic and API Key with **zero validator code**
- **Scalar UI protection**
- **API versioning integration**
- **Default authentication scheme** — a plain `[Authorize]` accepts Bearer, Basic and API Key
- **Per-operation security** — only protected endpoints show the lock in Scalar
- **JWKS endpoint** — publish your public keys so other services validate your tokens
- **Login endpoint with per-IP rate limiting** (development / testing convenience)

All features are enabled through a single **fluent builder**, and everything you use lives in one
namespace: `using Rkd.Scalar;`.

---

# Installation

```
dotnet add package Rkd.Scalar
```

---

# 30‑Second Setup

```csharp
builder.AddRkdScalar()
    .WithVersioning("v1")
    .WithBearerAuth();          // "Jwt" section of appsettings.json

var app = builder.Build();
// ...
app.UseRkdScalar();             // "RkdScalar" section of appsettings.json (optional)
```

```json
{
  "Jwt": {
    "Secret": "SUPER_SECRET_KEY_MINIMUM_32_CHARACTERS",
    "Issuer": "my-api",
    "Audience": "my-clients"
  }
}
```

Run the application and open **`/scalar`**.

`AddRkdScalar` and `UseRkdScalar` live in the standard `Microsoft.Extensions.Hosting` /
`Microsoft.AspNetCore.Builder` namespaces, so no `using` is needed in `Program.cs`.

---

# Quick Start

```csharp
using Rkd.Scalar;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddAuthorization();

builder.AddRkdScalar()
    .WithVersioning("v1")
    .WithUiProtection("admin", builder.Configuration["DocsPassword"]!)
    .WithBearerAuth<LoginRequest, LoginValidator>()          // "Jwt" section
    .WithJwtLoginEndpoint<LoginRequest>();                   // POST /auth/login, 5 attempts/min per IP

var app = builder.Build();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.UseRkdScalar(options => options.Title = "My API");

app.Run();
```

---

# Configuration via appsettings.json

`app.UseRkdScalar()` reads the `RkdScalar` section when it exists (all settings are optional):

```json
{
  "RkdScalar": {
    "Title": "My API",
    "Theme": "BluePlanet",
    "ScalarRoutePrefix": "/scalar",
    "OpenApiRoutePattern": "/openapi/{documentName}.json",
    "Enabled": true,
    "VersionSelector": true
  }
}
```

| Setting               | Default                         | Description                                                                 |
| --------------------- | ------------------------------- | --------------------------------------------------------------------------- |
| `Title`               | `API Documentation`             | Title shown by Scalar                                                       |
| `Theme`               | `Default`                       | Scalar theme                                                                |
| `ScalarRoutePrefix`   | `/scalar`                       | Route of the Scalar UI                                                      |
| `OpenApiRoutePattern` | `/openapi/{documentName}.json`  | Route of the OpenAPI documents (Scalar and UI protection follow it)         |
| `Enabled`             | `true`                          | `false` hides the UI and documents (authentication keeps working)           |
| `VersionSelector`     | `true`                          | Lists every API version in the Scalar dropdown (`false`: one page per version) |

Turning the documentation off in production is just configuration:

```json
// appsettings.Production.json
{ "RkdScalar": { "Enabled": false } }
```

```csharp
app.UseRkdScalar();                                        // "RkdScalar" section (or defaults)
app.UseRkdScalar(o => o.Title = "My API");                 // section + code
app.UseRkdScalar("Docs", o => o.Title = "Docs");          // any section + code
```

## Customizing the Scalar UI

`ConfigureScalar` gives direct access to Scalar's own options:

```csharp
using Scalar.AspNetCore;   // ScalarTheme and the ScalarOptions extension methods

app.UseRkdScalar(options =>
{
    options.Title = "My API";
    options.ConfigureScalar = scalar =>
    {
        scalar.Theme = ScalarTheme.BluePlanet;
        scalar.EnablePersistentAuthentication();
    };
});
```

---

# XML Comments

Your `///` comments show up in the OpenAPI documents and in Scalar **automatically**,
for every API version. Enable the documentation file in your API project (and in class
libraries that hold your models):

```xml
<PropertyGroup>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <NoWarn>$(NoWarn);CS1591</NoWarn>
</PropertyGroup>
```

```csharp
/// <summary>Gets an order.</summary>
/// <remarks>Only orders of the current tenant are returned.</remarks>
/// <param name="id">Order identifier.</param>
/// <response code="200">The order.</response>
/// <response code="404">Order not found.</response>
[HttpGet("{id}")]
public ActionResult<Order> Get(int id) { ... }
```

| XML tag                         | Where it goes in OpenAPI                        |
| ------------------------------- | ----------------------------------------------- |
| `<summary>` (action / handler)  | operation summary                               |
| `<remarks>`                     | operation description                           |
| `<param>`                       | parameter or request body description           |
| `<returns>`                     | success response description                    |
| `<response code="...">`         | description of that response                    |
| `<summary>` (class / property)  | schema / property description                   |

Works with controllers and with minimal APIs that use method handlers
(`app.MapGet("/ping", Handlers.Ping)`); lambdas have no XML comments. Descriptions set
explicitly (`WithSummary`, `[EndpointSummary]`…) are never overwritten. Turn it off with
`.WithXmlComments(false)`.

## Your own OpenAPI transformers

Register them once for every document (all versions):

```csharp
builder.AddRkdScalar()
    .WithVersioning("v1", "v2")
    .ConfigureOpenApi(options => options.AddDocumentTransformer<FormFileSchemaTransformer>());
```

---

# Standardized Errors (RFC 9457 Problem Details)

One line turns **every** error of the API into the standard
[RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) format, with the right HTTP status code:

```csharp
builder.AddRkdScalar().WithProblemDetails();
```

```http
HTTP/1.1 404 Not Found
Content-Type: application/problem+json

{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "detail": "Order 42 was not found.",
  "instance": "/api/v1/orders/42",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

What is covered, with no extra code:

| Source                                                   | Result                                                          |
| -------------------------------------------------------- | --------------------------------------------------------------- |
| Unhandled exception                                      | `500`, generic title — **no message or stack trace outside Development** |
| Mapped exception (`options.Map<T>(...)`)                 | Your status code, title and message                             |
| `throw new ProblemException(...)`                        | Exactly the status, title, detail, type and extensions you set  |
| Error responses without body (unknown route, 405, 415, 401/403 from authorization…) | Problem details with the same status code        |
| `Results.Problem`, `ValidationProblem`, `[ApiController]` model validation | Enriched with `instance` and `traceId`          |
| Malformed JSON / bad request binding                     | `400` problem details                                           |
| Client aborted the request                               | `499`, no error log                                             |

The response is always JSON (even for `Accept: text/html`) and the `traceId` matches your logs and
distributed traces. Responses produced from exceptions discard anything the failed request had written
and are sent with `Cache-Control: no-store`; for body-less responses, headers such as `WWW-Authenticate`
are preserved.

## Mapping your exceptions

```csharp
builder.AddRkdScalar().WithProblemDetails(options =>
{
    // status code: the exception message becomes the "detail" for 4xx
    options.Map<NotFoundException>(StatusCodes.Status404NotFound);
    options.Map<ConflictException>(StatusCodes.Status409Conflict, title: "Conflict");

    // 5xx never exposes the message unless you say so
    options.Map<PaymentGatewayException>(StatusCodes.Status502BadGateway, "Payment provider unavailable");

    // full control, including extensions
    options.Map<BusinessRuleException>(ex => new ProblemDetails
    {
        Status = StatusCodes.Status422UnprocessableEntity,
        Title = "Business rule violated",
        Detail = ex.Message,
        Type = "https://errors.mycompany.com/business-rule",
        Extensions = { ["rule"] = ex.RuleCode }
    });
});
```

Mappings match derived types (the most specific mapping wins). Framework exceptions such as
`KeyNotFoundException` or `ArgumentException` are **not** mapped by default, because they are often
thrown by bugs and their messages may contain internal details — map them explicitly if you want to.

## Throwing problems directly

```csharp
throw ProblemException.NotFound($"Order {id} was not found.");

throw new ProblemException(StatusCodes.Status409Conflict, "Duplicated order", $"Order {id} already exists.")
{
    Type = "https://errors.mycompany.com/duplicated-order",
    Extensions = { ["orderId"] = id }
};
```

## Options

| Option                    | Default               | Description                                                              |
| ------------------------- | --------------------- | ------------------------------------------------------------------------ |
| `IncludeExceptionDetails` | Development only      | Adds `detail` and an `exception` object (type, message, stack trace) to unexpected errors |
| `HandleStatusCodes`       | `true`                | Converts body-less 4xx/5xx responses; opt out per endpoint with `[SkipStatusCodePages]` |
| `IncludeInstance`         | `true`                | Sets `instance` to the request path                                      |
| `Customize`               | —                     | Adds or changes members of every problem (e.g. `service`, `tenant`)      |

```csharp
.WithProblemDetails(options =>
{
    options.Customize = context =>
        context.ProblemDetails.Extensions["service"] = "orders-api";
});
```

Logging: unexpected errors (5xx) are logged as `Error` with the exception; mapped client errors (4xx)
as `Information`; aborted requests as `Debug`. The middleware is placed at the beginning of the pipeline
automatically, so it also catches errors from other middlewares — no `UseExceptionHandler` needed. If
you add your own `app.UseExceptionHandler(...)`, yours runs first for the requests it handles.
In Development, ASP.NET Core's developer exception page still writes its own error log line, but the
response is the same problem details as in production (plus the exception details).

---

# API Versioning

```csharp
builder.AddRkdScalar()
    .WithVersioning("v1", "v2", "v3");

// customizing ApiVersioningOptions (default version, readers…)
builder.AddRkdScalar()
    .WithVersioning(o => o.DefaultApiVersion = new ApiVersion(2, 0), "v1", "v2");
```

```csharp
[ApiController]
[ApiVersion("1.0", Deprecated = true)]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/payments")]
public class PaymentController : ControllerBase { ... }
```

## Version selector

Every version is listed in a **dropdown at the top of the Scalar sidebar**:

- `/scalar` opens the newest non-deprecated version
- `/scalar/v1` (old links keep working) opens the dropdown with `v1` selected
- versions are listed newest first; deprecated ones show as `v1 (deprecated)`

Prefer one page per version? `{ "RkdScalar": { "VersionSelector": false } }`.

---

# Validators

Every authentication feature validates credentials with `ICredentialValidator<TCredentials>`,
which returns a `CredentialValidationResult`:

```csharp
using System.Security.Claims;
using Rkd.Scalar;

public sealed class LoginValidator(IUserRepository users, IPasswordHasher hasher)
    : ICredentialValidator<LoginRequest>
{
    public async Task<CredentialValidationResult> ValidateAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await users.FindByUsernameAsync(request.Username, cancellationToken);

        if (user is null || !hasher.Verify(request.Password, user.PasswordHash))
            return CredentialValidationResult.Failure("Invalid username or password.");

        if (user.IsLocked)
            return CredentialValidationResult.Failure("Account locked.");

        return CredentialValidationResult.Success(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            .. user.Roles.Select(role => new Claim(ClaimTypes.Role, role))
        ]);
    }
}
```

- `Failure(reason)` — the reason is logged by the authentication handlers and returned as the
  `detail` of the login endpoint's 401 response, so write something safe to show.
- A `ClaimsIdentity` converts implicitly to a result (`null` = failure), so `return identity;` works.

Credential models provided by the package: `BasicAuthCredentials(Username, Password)` and
`ApiKeyCredentials(Key)`. For JWT login you use your own model (`LoginRequest` above).

---

# JWT Authentication

## Two modes

```csharp
// 1) Validation only — tokens come from an identity provider or another service
builder.AddRkdScalar().WithBearerAuth();                                   // "Jwt" section
builder.AddRkdScalar().WithBearerAuth(new JwtOptions { ... });            // or in code

// 2) Validation + issuing — registers your validator and IJwtTokenService
builder.AddRkdScalar().WithBearerAuth<LoginRequest, LoginValidator>();   // "Jwt" section
```

## JWT settings

```json
{
  "Jwt": {
    "Secret": "SUPER_SECRET_KEY_MINIMUM_32_CHARACTERS",
    "Issuer": "my-api",
    "Audience": "my-clients",
    "ExpirationInMinutes": 60,
    "ClockSkewInSeconds": 30,
    "UseStandardClaimNames": true
  }
}
```

| Setting                  | Default    | Description                                                     |
| ------------------------ | ---------- | --------------------------------------------------------------- |
| `Issuer`                 | —          | `iss` — required (unless `Authority` is used)                   |
| `Audience`               | —          | `aud` — required                                                |
| `ExpirationInMinutes`    | `60`       | Token lifetime                                                  |
| `ClockSkewInSeconds`     | `30`       | Tolerance when validating `exp` / `nbf`                         |
| `UseStandardClaimNames`  | `true`     | `sub`, `name`, `role`… instead of the long .NET URIs            |
| `Algorithm`              | inferred   | e.g. `PS256`; when set, validation accepts only it              |
| `KeyId`                  | thumbprint | `kid` header (RSA/ECDSA keys)                                   |
| `Secret`, `PrivateKeyPem`, `PrivateKeyPath`, `PublicKeyPem`, `PublicKeyPath`, `Authority`, `MetadataAddress`, `RequireHttpsMetadata` | | Key sources, see below |

Missing or invalid settings (no key, no audience, non-positive expiration) fail at startup
with a descriptive message.

## Token contents

```json
{
  "iss": "my-api", "aud": "my-clients",
  "iat": 1790000000, "nbf": 1790000000, "exp": 1790003600,
  "jti": "5f0c…",
  "sub": "42", "name": "rodrigo", "role": ["admin", "reader"], "tenant": "acme"
}
```

`ClaimTypes.NameIdentifier`, `Name`, `Role`, `Email`, `GivenName`, `Surname`… are written with
their standard names and **mapped back** on validation, so `User.Identity.Name`,
`User.IsInRole("admin")`, `[Authorize(Roles = "admin")]` and
`User.FindFirst(ClaimTypes.NameIdentifier)` keep working. Custom claims (`tenant`) are kept as-is.

## Signing keys

`JwtOptions` accepts exactly **one** key source:

| Key source                               | Algorithm (inferred)       | Issues tokens | Validates tokens |
| ---------------------------------------- | -------------------------- | ------------- | ---------------- |
| `Secret` (≥ 32 chars)                    | HS256                      | ✔             | ✔                |
| `PrivateKeyPem` / `PrivateKeyPath`       | RS256 (RSA), ES256/384/512 | ✔             | ✔                |
| `PublicKeyPem` / `PublicKeyPath`         | RS256, ES256…              | ❌            | ✔                |
| `SigningKey` (`X509SecurityKey`, …)      | from the key               | ✔             | ✔                |
| `Authority` / `MetadataAddress` (OIDC)   | from the provider          | ❌            | ✔                |
| `IJwtSigner` (KMS / Key Vault / HSM)     | `IJwtSigner.Algorithm`     | ✔ (async)     | with a public key |

`ValidationKeys` accepts extra keys during **key rotation**.

### RSA / ECDSA

```json
{
  "Jwt": {
    "PrivateKeyPath": "/run/secrets/jwt-private.pem",
    "Issuer": "https://auth.mycompany.com",
    "Audience": "my-api",
    "ExpirationInMinutes": 30
  }
}
```

```csharp
builder.AddRkdScalar()
    .WithBearerAuth<LoginRequest, LoginValidator>()
    .WithJwksEndpoint();                                 // GET /.well-known/jwks.json
```

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out jwt-private.pem
openssl pkey -in jwt-private.pem -pubout -out jwt-public.pem
# or ECDSA P-256 (ES256)
openssl ecparam -name prime256v1 -genkey -noout -out jwt-private.pem
```

PEM content can also be passed inline (`PrivateKeyPem`, `PublicKeyPem`), including from
environment variables with escaped `\n` line breaks. Services that only validate:
`{ "Jwt": { "PublicKeyPath": "jwt-public.pem", "Issuer": "...", "Audience": "..." } }`.

### External identity provider (Keycloak, Entra ID, Auth0…)

```json
{ "Jwt": { "Authority": "https://keycloak.mycompany.com/realms/main", "Audience": "my-api" } }
```

Issuer and signing keys are downloaded (and rotated) from the discovery document.

### Async signing — KMS, Key Vault, HSM

```csharp
using Azure.Security.KeyVault.Keys.Cryptography;
using Rkd.Scalar;

public sealed class KeyVaultJwtSigner(CryptographyClient client) : IJwtSigner
{
    public string Algorithm => "RS256";
    public string? KeyId => "my-key-v1";

    public async Task<byte[]> SignAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        var result = await client.SignDataAsync(SignatureAlgorithm.RS256, data, cancellationToken);
        return result.Signature;
    }
}

builder.Services.AddSingleton(new CryptographyClient(keyId, new DefaultAzureCredential()));

builder.AddRkdScalar()
    .WithBearerAuth<LoginRequest, LoginValidator>()   // "Jwt": { "PublicKeyPath": "...", "KeyId": "my-key-v1", ... }
    .WithJwtSigner<KeyVaultJwtSigner>();
```

> ECDSA signatures must use the IEEE P1363 format (R‖S) required by RFC 7518 — AWS KMS returns DER.

To replace token issuing entirely: `.WithJwtTokenService<MyTokenService>()`.

## Your own login endpoint (recommended for production)

`WithBearerAuth<TCredentials, TValidator>()` registers your validator and `IJwtTokenService`:

```csharp
[ApiController]
[Route("auth")]
public sealed class AuthController(
    ICredentialValidator<LoginRequest> validator,
    IJwtTokenService tokens) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);

        if (!result.Succeeded)
            return Problem(statusCode: 401, detail: result.FailureReason);

        var token = await tokens.CreateTokenAsync(result.Identity, cancellationToken: cancellationToken);

        // { access_token, token_type, expires_in, expires_at }
        return Ok(JwtLoginResponse.FromToken(token));
    }
}
```

`JwtToken` also exposes `TokenId` (`jti`), `IssuedAt` and `ExpiresAt` for auditing or revocation.

Production checklist: load secrets and private keys from a secret store, prefer asymmetric keys when
several services validate the tokens, hash passwords, apply your own rate limiting policy and log
failed attempts.

## Built-in login endpoint (development / testing)

```csharp
.WithBearerAuth<LoginRequest, LoginValidator>()
.WithJwtLoginEndpoint<LoginRequest>()                                   // POST /auth/login, 5/min per IP
.WithJwtLoginEndpoint<LoginRequest>("/token", 10, TimeSpan.FromMinutes(5))
.WithJwtLoginEndpoint<LoginRequest>("/token", "my-rate-limit-policy")   // your own policy
```

```json
// 200
{ "access_token": "…", "token_type": "Bearer", "expires_in": 3600, "expires_at": "2026-01-01T12:00:00+00:00" }

// 401 (application/problem+json)
{ "status": 401, "title": "Invalid credentials", "detail": "Account locked." }
```

The endpoint is rate limited **per client IP** (`HTTP 429` with `Retry-After` when exceeded).
Requires `app.UseRateLimiter()` before `app.UseAuthentication()`; behind a reverse proxy,
configure `ForwardedHeaders` so the real client IP is used. The credential type must be the one
used in `WithBearerAuth<TCredentials, TValidator>()` (checked at startup).

---

# Basic and API Key Authentication

Every scheme can be configured with your own validator or straight from configuration:

| Feature       | With your validator                         | From configuration (no code)                                         |
| ------------- | ------------------------------------------- | -------------------------------------------------------------------- |
| UI protection | `WithUiProtection<TValidator>()`            | `WithUiProtection("UiCredentials")` or `WithUiProtection(user, pwd)` |
| Basic         | `WithBasicAuth<TValidator>()`               | `WithBasicAuth("BasicAuth")`                                         |
| API Key       | `WithApiKeyAuth<TValidator>(configure?)`    | `WithApiKeyAuth("ApiKeys", configure?)`                              |

Configuration-based validators compare secrets in constant time, treat usernames
case-insensitively, re-read the section on each request and fail at startup when the section is missing.

```json
{
  "BasicAuth": { "Username": "service-user", "Password": "service-password" },
  "ApiKeys": {
    "partner-a": "key-for-partner-a",
    "partner-b": { "Key": "key-for-partner-b", "Roles": [ "reader" ] }
  }
}
```

```csharp
builder.AddRkdScalar()
    .WithBasicAuth()
    .WithApiKeyAuth(configure: o => o.HeaderName = "X-Partner-Key");   // default header: X-API-Key
```

`ApiKeys` also accepts a plain list: `"ApiKeys": [ "key-1", "key-2" ]`. The client name becomes
`User.Identity.Name` and `Roles` become role claims.

---

# Securing Endpoints

With more than one scheme, ASP.NET Core has no default scheme, so endpoints name them:

```csharp
[Authorize(AuthenticationSchemes = RkdScalarAuthenticationSchemes.All)]   // "Bearer,Basic,ApiKey"
```

Or let Rkd.Scalar pick the scheme from the request, so a plain `[Authorize]` works:

```csharp
builder.AddRkdScalar()
    .WithBearerAuth()
    .WithApiKeyAuth()
    .WithDefaultAuthenticationScheme();

app.MapGet("/orders", () => ...).RequireAuthorization();
```

## Per-operation security in the documentation

With `WithOperationSecurity()` only endpoints that require authorization show the lock,
`[AllowAnonymous]` endpoints show none and `[Authorize(AuthenticationSchemes = "Bearer")]`
offers only Bearer.

---

# Protecting the Scalar UI

Protects the UI and the OpenAPI documents (the API itself is not affected):

```csharp
builder.AddRkdScalar().WithUiProtection();                    // "UiCredentials" section
builder.AddRkdScalar().WithUiProtection("Docs:Users");        // custom section
builder.AddRkdScalar().WithUiProtection("admin", password);   // single user
builder.AddRkdScalar().WithUiProtection<MyValidator>();       // your own logic
```

```json
{ "UiCredentials": { "alice": "password1", "bob": "password2" } }
```

The section also accepts `{ "Username": "...", "Password": "..." }`. Protection follows
`ScalarRoutePrefix` and `OpenApiRoutePattern`.

---

# Builder API Reference

| Method                                                              | What it does                                                   |
| ------------------------------------------------------------------- | -------------------------------------------------------------- |
| `WithVersioning("v1", ...)`                                         | API versioning + one OpenAPI document per version              |
| `WithXmlComments(enabled)`                                          | XML comments in the documents (on by default)                  |
| `ConfigureOpenApi(configure)`                                       | Your own OpenAPI transformers/options for every document       |
| `WithUiProtection(...)`                                             | Basic Auth in front of the UI and OpenAPI documents            |
| `WithBearerAuth(options \| section)`                                | JWT validation (HMAC, RSA, ECDSA, OIDC authority)              |
| `WithBearerAuth<TCredentials, TValidator>(options \| section)`      | JWT validation + token issuing (`IJwtTokenService`)            |
| `WithJwtLoginEndpoint<TCredentials>(...)`                           | Login endpoint with per-IP rate limiting                       |
| `WithJwtSigner<TSigner>()`                                          | Async/remote token signing (KMS, Key Vault, HSM)               |
| `WithJwtTokenService<TService>()`                                   | Replaces the token issuer                                      |
| `WithJwksEndpoint(path)`                                            | Publishes the public keys (`/.well-known/jwks.json`)           |
| `WithBasicAuth<TValidator>()` / `WithBasicAuth(section)`            | Basic authentication for the API                               |
| `WithApiKeyAuth<TValidator>(configure?)` / `WithApiKeyAuth(section, configure?)` | API Key authentication                            |
| `WithDefaultAuthenticationScheme()`                                 | Plain `[Authorize]` accepts every enabled scheme               |
| `WithOperationSecurity()`                                           | Security requirements only on protected operations            |
| `WithProblemDetails(configure?)`                                    | RFC 9457 errors for exceptions and error responses             |
| `WithLowercaseRouting()`                                            | Lowercase URLs and query strings                               |

---

# Production Example

```csharp
builder.AddRkdScalar()
    .WithVersioning("v1", "v2", "v3")
    .WithUiProtection()                                      // "UiCredentials" section
    .WithBearerAuth<LoginRequest, LoginValidator>()          // "Jwt" section (RSA key)
    .WithJwksEndpoint()
    .WithApiKeyAuth()                                        // "ApiKeys" section
    .WithDefaultAuthenticationScheme()
    .WithOperationSecurity()
    .WithProblemDetails(o => o.Map<NotFoundException>(StatusCodes.Status404NotFound))
    .WithLowercaseRouting();
```

# Launch Scalar Automatically

In `Properties/launchSettings.json`: `"launchBrowser": true, "launchUrl": "scalar"`.

---

# Compatibility

- .NET 10, Minimal APIs and Controllers
- Every release is checked with
  [package validation](https://learn.microsoft.com/dotnet/fundamentals/apicompat/package-validation/overview):
  public APIs only break in major versions, with a [migration guide](https://github.com/rkdcoder/Rkd.Scalar/blob/master/MIGRATION.md).

See the [CHANGELOG](https://github.com/rkdcoder/Rkd.Scalar/blob/master/CHANGELOG.md) for release history.

---

# Contributing

Pull requests are welcome. Open an issue to propose new features or improvements.

# License

MIT License

---

Built on the belief that API documentation should take minutes, not hours.
