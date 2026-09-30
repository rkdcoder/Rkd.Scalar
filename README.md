# Rkd.Scalar - API Documentation Platform for ASP.NET

<p align="center">
  <img src="https://raw.githubusercontent.com/rkdcoder/Rkd.Scalar/master/src/Rkd.Scalar/Media/icon.png" width="128" alt="Rkd.Scalar logo" />
</p>

[![NuGet](https://img.shields.io/nuget/v/Rkd.Scalar.svg)](https://www.nuget.org/packages/Rkd.Scalar)
[![Build & Publish](https://github.com/rkdcoder/Rkd.Scalar/actions/workflows/main.yml/badge.svg)](https://github.com/rkdcoder/Rkd.Scalar/actions/workflows/main.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/rkdcoder/Rkd.Scalar/blob/master/LICENSE)

**Rkd.Scalar** adds a production-ready API documentation platform.
It combines documentation, authentication helpers and security protections into a single fluent configuration layer.

---

# Core Features

Rkd.Scalar integrates:

- **Scalar UI**
- **OpenAPI generation** with **XML comments applied automatically** (every API version, no `AddOpenApi` needed)
- **JWT Bearer authentication** — HMAC secret, **RSA / ECDSA keys**, **OIDC authority (JWKS)** or **async / KMS signing**
- **API Key authentication**
- **Basic authentication**
- **API versioning integration**
- **Scalar UI protection**
- **Configuration-based validators** — protect the UI, Basic and API Key with **zero validator code**
- **Default authentication scheme** — a plain `[Authorize]` accepts Bearer, Basic and API Key
- **Per-operation security** — only protected endpoints show the lock in Scalar
- **JWKS endpoint** — publish your public keys so other services validate your tokens
- **Default JWT login endpoint with built-in rate limiting** (development / testing convenience)

All features are enabled through a simple **fluent builder API**.

---

# Why Rkd.Scalar

Rkd.Scalar focuses on three principles:

- **Simplicity** – drastically reduce OpenAPI setup code
- **Security** – protect documentation and test endpoints safely
- **Extensibility** – modular feature-based architecture

---

# Who is this for?

- Teams building internal APIs
- SaaS platforms exposing partner APIs
- Microservices that validate tokens issued by an identity provider (Keycloak, Entra ID, Auth0…)
- Developers who want production-ready documentation fast
- Teams tired of complex Swagger configuration

---

# Installation

Install via .NET CLI:

```
dotnet add package Rkd.Scalar
```

Or via Package Manager:

```
Install-Package Rkd.Scalar
```

---

# 30‑Second Setup

```csharp
builder.AddRkdScalar()
    .WithVersioning("v1")
    .WithBearerAuth();              // JwtOptions bound from appsettings.json
```

```csharp
app.UseRkdScalar();                 // RkdScalar section of appsettings.json (optional)
```

Run the application and open:

```
/scalar/v1
```

You now have:

- OpenAPI documentation
- Scalar UI
- JWT authentication

> `builder.AddRkdScalar()` is a shortcut for
> `builder.Services.AddRkdScalar(builder.Configuration)` — both keep working.

---

# Quick Start

## Without Rkd.Scalar

```
200+ lines of configuration
OpenAPI
JWT
Auth schemes
Versioning
Security definitions
```

## With Rkd.Scalar

Minimal setup example (development):

```csharp
using Rkd.Scalar.Extensions;
using Rkd.Scalar.Security.Jwt;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddAuthorization();

var jwtOptions = new JwtOptions
{
    Secret = "SUPER_SECRET_KEY_MINIMUM_32_CHARACTERS",
    Issuer = "MyApi",
    Audience = "MyApiClient",
    Expiration = TimeSpan.FromHours(2),
    ValidateNotBefore = true
};

builder.AddRkdScalar()
    .WithVersioning("v1")
    .WithUiProtection("admin", "change-me")
    .WithBearerAuth<AuthCredential, LoginValidator>(jwtOptions)
    .WithDefaultJwtLogin<AuthCredential>("/auth/login", 5, TimeSpan.FromMinutes(1));

var app = builder.Build();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.UseRkdScalar(new RkdScalarConfiguration { Title = "My API" });

app.Run();
```

> **Note:** hardcoding secrets is shown here only for brevity. In real
> applications, load them from environment variables, user secrets or a secret
> manager — never commit secrets to source control.

---

# Configuration via appsettings.json

`app.UseRkdScalar()` (no arguments) reads the `RkdScalar` section when it exists:

```json
{
  "RkdScalar": {
    "Title": "My API",
    "Theme": "BluePlanet",
    "ScalarRoutePrefix": "/scalar",
    "OpenApiRoutePattern": "/openapi/{documentName}.json",
    "Enabled": true
  }
}
```

| Setting               | Default                         | Description                                                                 |
| --------------------- | ------------------------------- | --------------------------------------------------------------------------- |
| `Title`               | `API Documentation.`            | Title shown by Scalar                                                       |
| `Theme`               | `Default`                       | Scalar theme                                                                |
| `ScalarRoutePrefix`   | `/scalar`                       | Route of the Scalar UI                                                      |
| `OpenApiRoutePattern` | `/openapi/{documentName}.json`  | Route of the OpenAPI documents (Scalar and UI protection follow it)         |
| `Enabled`             | `true`                          | `false` hides the UI and documents (authentication keeps working)           |

Turning the documentation off in production is just configuration:

```json
// appsettings.Production.json
{ "RkdScalar": { "Enabled": false } }
```

The three `UseRkdScalar` overloads:

```csharp
app.UseRkdScalar();                                                 // "RkdScalar" section (or defaults)
app.UseRkdScalar("Docs", o => o.ConfigureScalar = s => s.DarkMode = true); // any section + code
app.UseRkdScalar(new RkdScalarConfiguration { Title = "My API" });      // code only
```

---

# XML Comments

Your `///` comments show up in the OpenAPI documents and in Scalar **automatically**,
for every API version. Just enable the documentation file in your API project
(and in class libraries that hold your models):

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
(`app.MapGet("/ping", Handlers.Ping)`); lambdas have no XML comments. Descriptions
set explicitly (`WithSummary`, `[EndpointSummary]`…) are never overwritten.

> Before 1.2.0 you had to add `builder.Services.AddOpenApi("v1")` (one per version)
> so the ASP.NET Core source generator would pick up the comments. That line is no
> longer needed; keeping it is harmless (nothing is duplicated). To turn the
> feature off: `.WithXmlComments(false)`.

## Your own OpenAPI transformers

Register them once for every document (all versions) with `ConfigureOpenApi`:

```csharp
builder.AddRkdScalar()
    .WithVersioning("v1", "v2")
    .ConfigureOpenApi(options => options.AddDocumentTransformer<FormFileSchemaTransformer>());
```

---

# Customizing the Scalar UI

Rkd.Scalar allows full customization of Scalar through `ConfigureScalar`:

```csharp
app.UseRkdScalar(new RkdScalarConfiguration
{
    Title = "My API",
    ConfigureScalar = scalar =>
    {
        scalar.DarkMode = true;
        scalar.Theme = ScalarTheme.BluePlanet;
        scalar.EnablePersistentAuthentication();
    }
});
```

This gives direct access to **ScalarOptions** while still keeping Rkd.Scalar's simplified configuration.

---

# Why not Swashbuckle?

| Feature                        | Swashbuckle | Rkd.Scalar |
| ------------------------------ | ----------- | ---------- |
| **OpenAPI generation**         | ✔           | ✔          |
| **Scalar UI**                  | ❌          | ✔          |
| **JWT (HMAC / RSA / ECDSA)**   | manual      | built-in   |
| **JWKS endpoint**              | ❌          | ✔          |
| **JWT login endpoint**         | ❌          | ✔ (dev)    |
| **API Key auth**               | manual      | built-in   |
| **UI protection**              | ❌          | ✔          |
| **Versioning integration**     | manual      | built-in   |

---

# JWT Authentication

Rkd.Scalar supports two JWT modes:

## 1) Validation-only (no login endpoint)

Use this mode when your API only validates bearer tokens issued elsewhere
(an identity provider, an auth microservice, another API).

```csharp
builder.AddRkdScalar()
    .WithBearerAuth(jwtOptions);
```

## 2) JWT with credential validation (token issuing)

Use this mode when your API itself issues tokens. It registers:

- `ICredentialValidator<TCredential>` (your implementation)
- `IJwtTokenService` (token generation)

```csharp
builder.AddRkdScalar()
    .WithBearerAuth<AuthCredential, LoginValidator>(jwtOptions);
```

From here you have two options for the login endpoint:

- **Production:** implement your **own** login endpoint (recommended — see below)
- **Development / testing:** use `WithDefaultJwtLogin` for a zero-code endpoint

---

# JWT Signing Keys

`JwtOptions` accepts exactly **one** key source:

| Key source                               | Algorithm (inferred)       | Issues tokens | Validates tokens |
| ---------------------------------------- | -------------------------- | ------------- | ---------------- |
| `Secret` (≥ 32 chars)                    | HS256                      | ✔             | ✔                |
| `PrivateKeyPem` / `PrivateKeyPath`       | RS256 (RSA), ES256/384/512 | ✔             | ✔                |
| `PublicKeyPem` / `PublicKeyPath`         | RS256, ES256…              | ❌            | ✔                |
| `SigningKey` (`X509SecurityKey`, …)      | from the key               | ✔             | ✔                |
| `Authority` / `MetadataAddress` (OIDC)   | from the provider          | ❌            | ✔                |
| `IJwtSigner` (KMS / Key Vault / HSM)     | `IJwtSigner.Algorithm`     | ✔ (async)     | with a public key |

Other options: `Algorithm` (e.g. `PS256`; when set, validation accepts only it),
`KeyId` (the `kid` header — RSA/ECDSA keys get their RFC 7638 thumbprint by default),
`ValidationKeys` (extra keys accepted during **key rotation**), `ClockSkew` and
`RequireHttpsMetadata`.

## HMAC secret (shared key)

```csharp
new JwtOptions
{
    Secret = builder.Configuration["Jwt:Secret"]!,
    Issuer = "MyApi",
    Audience = "MyApiClient",
    Expiration = TimeSpan.FromHours(2)
};
```

## RSA / ECDSA (asymmetric)

The API that issues tokens holds the private key; every other service only needs
the public key (or the JWKS endpoint).

```json
{
  "JwtOptions": {
    "PrivateKeyPath": "/run/secrets/jwt-private.pem",
    "Issuer": "https://auth.mycompany.com",
    "Audience": "my-api",
    "ExpirationMinutes": 30
  }
}
```

```csharp
builder.AddRkdScalar()
    .WithBearerAuth<AuthCredential, LoginValidator>()   // binds "JwtOptions"
    .WithJwksEndpoint();                                 // GET /.well-known/jwks.json
```

Generate keys with OpenSSL:

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out jwt-private.pem
openssl pkey -in jwt-private.pem -pubout -out jwt-public.pem
# or ECDSA P-256 (ES256)
openssl ecparam -name prime256v1 -genkey -noout -out jwt-private.pem
```

PEM content can also be passed inline (`PrivateKeyPem`, `PublicKeyPem`), including
from environment variables with escaped `\n` line breaks.

Services that only **validate** these tokens:

```json
{ "JwtOptions": { "PublicKeyPath": "jwt-public.pem", "Issuer": "https://auth.mycompany.com", "Audience": "my-api" } }
```

## External identity provider (Keycloak, Entra ID, Auth0, your own JWKS)

```json
{
  "JwtOptions": {
    "Authority": "https://keycloak.mycompany.com/realms/main",
    "Audience": "my-api"
  }
}
```

```csharp
builder.AddRkdScalar().WithBearerAuth();
```

Issuer and signing keys are downloaded (and rotated) from the provider's
discovery document. Use `MetadataAddress` when the discovery document is not at
`{Authority}/.well-known/openid-configuration`.

## Async signing — KMS, Key Vault, HSM

When the private key must never leave a vault, implement `IJwtSigner`. Rkd.Scalar
builds the header and payload and only delegates the signature — asynchronously.

```csharp
using Azure.Security.KeyVault.Keys.Cryptography;
using Rkd.Scalar.Security.Jwt;

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
```

```csharp
builder.Services.AddSingleton(new CryptographyClient(keyId, new DefaultAzureCredential()));

builder.AddRkdScalar()
    .WithBearerAuth<AuthCredential, LoginValidator>(new JwtOptions
    {
        PublicKeyPath = "jwt-public.pem",   // used to validate the tokens this API issues
        KeyId = "my-key-v1",
        Issuer = "MyApi",
        Audience = "MyApiClient"
    })
    .WithJwtSigner<KeyVaultJwtSigner>();
```

> For ECDSA algorithms the signature must be in IEEE P1363 format (R‖S), as
> required by RFC 7518 — AWS KMS returns DER and needs conversion.

Use `await jwtService.GenerateTokenAsync(identity, cancellationToken: ct)` in your
own endpoints (the synchronous `GenerateToken` keeps working, but blocks when an
`IJwtSigner` is registered). To replace token issuing entirely, use
`.WithJwtTokenService<MyTokenService>()`.

---

# JWT Options via appsettings.json

Instead of building `JwtOptions` manually, bind it from a configuration section
using the section-name overloads:

```json
{
  "JwtOptions": {
    "Secret": "SUPER_SECRET_KEY_MINIMUM_32_CHARACTERS",
    "Issuer": "MyApi",
    "Audience": "MyApiClient",
    "Expiration": 2,
    "ValidateNotBefore": true
  }
}
```

`Expiration` is expressed in **hours**; use `ExpirationMinutes` for shorter
lifetimes. All key options above (`PrivateKeyPem`, `PrivateKeyPath`,
`PublicKeyPem`, `PublicKeyPath`, `Algorithm`, `KeyId`, `Authority`,
`MetadataAddress`, `RequireHttpsMetadata`, `ClockSkewSeconds`) can be bound too.

```csharp
// Validation-only mode, bound from the "JwtOptions" section (default name)
builder.AddRkdScalar().WithBearerAuth();

// Or with credential validation, using a custom section name
builder.AddRkdScalar().WithBearerAuth<AuthCredential, LoginValidator>("Auth:Jwt");
```

If the section is missing or invalid (no key, non-positive expiration), the
application fails fast at startup with a descriptive error.

---

# JWT in Production — Custom Login Endpoint

**This is the recommended approach for production.**

`WithBearerAuth<TCredential, TValidator>()` registers everything you need to
issue tokens from your own endpoint: your `ICredentialValidator<TCredential>`
and the `IJwtTokenService`. You keep full control over the route, versioning,
rate limiting policy, logging, auditing and response shape.

```csharp
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rkd.Scalar.Security.Contracts;
using Rkd.Scalar.Security.Jwt;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController(
    ICredentialValidator<AuthCredential> validator,
    IJwtTokenService jwtService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] AuthCredential credential,
        CancellationToken cancellationToken)
    {
        var identity = await validator.ValidateAsync(credential, cancellationToken);

        if (identity is null)
            return Unauthorized();

        var token = await jwtService.GenerateTokenAsync(identity, cancellationToken: cancellationToken);

        // { access_token, token_type, expires_in, expires_at }
        return Ok(JwtLoginResponse.FromResult(token));
    }
}
```

## Production-grade validator

In production, the validator should check credentials against a real user
store using password hashing — never plaintext comparison:

```csharp
public sealed class LoginValidator(IUserRepository users, IPasswordHasher hasher)
    : ICredentialValidator<AuthCredential>
{
    public async Task<ClaimsIdentity?> ValidateAsync(
        AuthCredential request,
        CancellationToken cancellationToken = default)
    {
        var user = await users.FindByUsernameAsync(request.Username, cancellationToken);

        if (user is null || !hasher.Verify(request.Password, user.PasswordHash))
            return null;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username)
        };

        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

        return new ClaimsIdentity(claims, "Bearer");
    }
}
```

`IUserRepository` and `IPasswordHasher` represent your own persistence and
hashing infrastructure (e.g. `Microsoft.AspNetCore.Identity.PasswordHasher<T>`,
BCrypt or Argon2).

## Production checklist

- Load secrets and private keys from environment variables, mounted files or a secret manager
- Prefer asymmetric keys (RSA/ECDSA) when more than one service validates the tokens
- Hash and verify passwords — never store or compare plaintext
- Apply your own rate limiting policy to the login route
- Log failed authentication attempts for auditing
- Consider refresh tokens and token revocation if your scenario requires them

---

# Default JWT Login Endpoint (Development / Testing)

> ⚠️ **This feature is intended for development, testing and prototyping —
> not for production.** For production, build your own endpoint as shown in
> [JWT in Production](#jwt-in-production--custom-login-endpoint).

```csharp
.WithBearerAuth<AuthCredential, LoginValidator>(jwtOptions)
.WithDefaultJwtLogin<AuthCredential>("/auth/login", 5, TimeSpan.FromMinutes(1))
```

This creates `POST /auth/login`. The request body is bound to the credential
model (`TCredential`), which must be the same type used in
`WithBearerAuth<TCredential, TValidator>()` — a mismatch fails at startup.

Example request:

```json
{ "username": "admin", "password": "123" }
```

Example response:

```json
{
  "access_token": "JWT_TOKEN",
  "token_type": "Bearer",
  "expires_in": 7200,
  "expires_at": "2026-01-01T12:00:00Z"
}
```

## Built-in Brute Force Protection

The endpoint configures **ASP.NET Rate Limiting**, partitioned **per client IP**:
in the example above each client gets at most **5 attempts per minute**; beyond
that the API returns `HTTP 429 Too Many Requests` with a `Retry-After` header.

Prefer your own policy? Pass its name instead:

```csharp
builder.Services.AddRateLimiter(o => o.AddSlidingWindowLimiter("login", ...));

.WithDefaultJwtLogin<AuthCredential>("/auth/login", "login")
```

Don't forget `app.UseRateLimiter();` before `app.UseAuthentication();`.

> Behind a reverse proxy, configure `ForwardedHeaders` so the client IP is the
> real one — otherwise every request shares the proxy IP.

---

# Authentications

`ICredentialValidator<T>` is **provided by the Rkd.Scalar NuGet package**
(`Rkd.Scalar.Security.Contracts`). When implementing validators, use the
interface from the package.

Every scheme can be configured in two ways:

| Scheme       | With your validator                   | From configuration (no code)          |
| ------------ | ------------------------------------- | ------------------------------------- |
| UI protection| `WithUiProtection<TValidator>()`      | `WithUiProtection("UiCredentials")` or `WithUiProtection(user, password)` |
| Basic        | `WithBasicAuth<TValidator>()`         | `WithBasicAuth("BasicAuth")`          |
| API Key      | `WithApiKeyAuth<TValidator>()`        | `WithApiKeyAuth("ApiKeys")`           |

Configuration-based validators compare secrets in constant time, treat usernames
case-insensitively, re-read the section on each request (reloadable
configuration works) and fail fast at startup when the section is missing.

## Basic Authentication

From configuration:

```json
{
  "BasicAuth": {
    "Username": "service-user",
    "Password": "service-password"
  }
}
```

```csharp
builder.AddRkdScalar().WithBasicAuth();
```

Or with your own validator:

```csharp
public class BasicValidator : ICredentialValidator<BasicAuthCredentials>
{
    public Task<ClaimsIdentity?> ValidateAsync(
        BasicAuthCredentials request,
        CancellationToken cancellationToken = default)
    {
        // validate against your user store
    }
}

builder.AddRkdScalar().WithBasicAuth<BasicValidator>();
```

## API Key Authentication

Requests must include the header `X-API-Key: YOUR_API_KEY` (configurable).

From configuration — any of these formats:

```json
{
  "ApiKeys": {
    "partner-a": "key-for-partner-a",
    "partner-b": { "Key": "key-for-partner-b", "Roles": [ "reader" ] }
  }
}
```

```json
{ "ApiKeys": [ "key-1", "key-2" ] }
```

```csharp
builder.AddRkdScalar().WithApiKeyAuth();

// custom header
builder.AddRkdScalar().WithApiKeyAuth(configure: o => o.HeaderName = "X-Partner-Key");
```

The client name becomes the user name (`User.Identity.Name`) and `Roles` become
role claims.

Or with your own validator:

```csharp
public class ApiKeyValidator : ICredentialValidator<ApiKeyCredentials>
{
    public Task<ClaimsIdentity?> ValidateAsync(
        ApiKeyCredentials request,
        CancellationToken cancellationToken = default)
    {
        // validate request.Key against a secure store
    }
}

builder.AddRkdScalar().WithApiKeyAuth<ApiKeyValidator>();
builder.AddRkdScalar().WithApiKeyAuth<ApiKeyValidator>(o => o.HeaderName = "X-Partner-Key");
```

The API Key scheme automatically appears in the **Scalar authentication panel**.

---

# Securing Endpoints

With more than one scheme, ASP.NET Core has no default scheme, so endpoints must
name them — use the constants in `RkdScalarAuthenticationSchemes`:

```csharp
[Authorize(AuthenticationSchemes = RkdScalarAuthenticationSchemes.All)]   // "Bearer,Basic,ApiKey"
[HttpGet("secure")]
public IActionResult SecureEndpoint() => Ok("Authorized access");
```

Or let Rkd.Scalar choose the scheme from the request, so a plain `[Authorize]`
works with every enabled scheme:

```csharp
builder.AddRkdScalar()
    .WithBearerAuth()
    .WithApiKeyAuth()
    .WithDefaultAuthenticationScheme();

app.MapGet("/orders", () => ...).RequireAuthorization();
```

## Per-operation security in the documentation

By default every operation shows every scheme. With `WithOperationSecurity()`
only endpoints that require authorization show the lock, `[AllowAnonymous]`
endpoints show none and `[Authorize(AuthenticationSchemes = "Bearer")]` offers
only Bearer:

```csharp
builder.AddRkdScalar()
    .WithBearerAuth()
    .WithApiKeyAuth()
    .WithOperationSecurity();
```

---

# Protecting the Scalar UI

To require authentication before accessing the documentation UI and the
OpenAPI documents (the API itself is not affected):

```json
{
  "UiCredentials": {
    "alice": "password1",
    "bob": "password2"
  }
}
```

```csharp
builder.AddRkdScalar().WithUiProtection();            // reads "UiCredentials"
builder.AddRkdScalar().WithUiProtection("Docs:Users"); // custom section
builder.AddRkdScalar().WithUiProtection("admin", builder.Configuration["DocsPassword"]!);
```

The section also accepts the single-user format
`{ "Username": "myuser", "Password": "mypassword" }`.

Custom logic (a database, an LDAP…)? Implement
`ICredentialValidator<BasicAuthCredentials>` and use
`WithUiProtection<TValidator>()`.

Protection follows `ScalarRoutePrefix` and `OpenApiRoutePattern`, so custom
documentation routes are protected too.

---

# API Versioning

Rkd.Scalar integrates with **Asp.Versioning** automatically.

```csharp
builder.AddRkdScalar()
    .WithVersioning("v1", "v2", "v3");

// customizing ApiVersioningOptions (default version, readers…)
builder.AddRkdScalar()
    .WithVersioning(o => o.DefaultApiVersion = new ApiVersion(2, 0), "v1", "v2");
```

```csharp
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments")]
public class PaymentController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok("API v1 running");
}
```

Scalar automatically generates a document for each version.

---

# Launch Scalar Automatically

In `Properties/launchSettings.json`:

```json
"launchBrowser": true,
"launchUrl": "scalar/v1"
```

---

# Builder API Reference

| Method                                                   | What it does                                                               |
| -------------------------------------------------------- | -------------------------------------------------------------------------- |
| `WithVersioning("v1", ...)`                              | API versioning + one OpenAPI document per version                          |
| `WithUiProtection<TValidator>()` / `WithUiProtection(section)` / `WithUiProtection(user, pwd)` | Basic Auth in front of the UI and OpenAPI documents |
| `WithBearerAuth(options)` / `WithBearerAuth(section)`    | JWT validation (HMAC, RSA, ECDSA, OIDC authority)                          |
| `WithBearerAuth<TCredential, TValidator>(...)`           | JWT validation + token issuing (`IJwtTokenService`)                        |
| `WithJwtSigner<TSigner>()`                               | Async/remote token signing (KMS, Key Vault, HSM)                           |
| `WithJwtTokenService<TService>()`                        | Replaces the token issuer                                                  |
| `WithJwksEndpoint(path)`                                 | Publishes the public keys (`/.well-known/jwks.json`)                       |
| `WithDefaultJwtLogin<TCredential>(path, limit, window)`  | Dev login endpoint with per-IP rate limiting                               |
| `WithDefaultJwtLogin<TCredential>(path, policy)`         | Dev login endpoint with your rate limiting policy                          |
| `WithBasicAuth<TValidator>()` / `WithBasicAuth(section)` | Basic authentication for the API                                           |
| `WithApiKeyAuth<TValidator>(configure?)` / `WithApiKeyAuth(section, configure?)` | API Key authentication                             |
| `WithDefaultAuthenticationScheme()`                      | Plain `[Authorize]` accepts every enabled scheme                           |
| `WithOperationSecurity()`                                | Security requirements only on protected operations                         |
| `WithXmlComments(enabled)`                               | XML comments in the documents (on by default)                              |
| `ConfigureOpenApi(configure)`                            | Your own OpenAPI transformers/options for every document                   |
| `WithLowercaseRouting()`                                 | Lowercase URLs and query strings                                           |

---

# Production Example

A realistic production setup issues tokens through **your own** login endpoint
and does **not** use `WithDefaultJwtLogin`:

```csharp
builder.AddRkdScalar()
    .WithVersioning("v1", "v2", "v3")
    .WithUiProtection()                                          // "UiCredentials" section
    .WithBearerAuth<AuthCredential, LoginValidator>()            // "JwtOptions" section (RSA key)
    .WithJwksEndpoint()
    .WithApiKeyAuth()                                            // "ApiKeys" section
    .WithDefaultAuthenticationScheme()
    .WithOperationSecurity()
    .WithLowercaseRouting();
```

If your API does not issue tokens at all (they come from an identity provider):

```csharp
builder.AddRkdScalar().WithBearerAuth();   // "JwtOptions": { "Authority": "...", "Audience": "..." }
```

# Development Example

```csharp
builder.AddRkdScalar()
    .WithVersioning("v1")
    .WithBearerAuth<AuthCredential, LoginValidator>(jwtOptions)
    .WithDefaultJwtLogin<AuthCredential>("/auth/login", 5, TimeSpan.FromMinutes(1));
```

---

# Feature‑Based Architecture

Each capability (JWT, Basic Auth, API Key Auth, Versioning, UI protection…) is
implemented as an independent feature, which makes the library easy to extend,
maintain and evolve.

```
Rkd.Scalar
├── Builder         fluent API
├── Configuration   UseRkdScalar options
├── Extensions      AddRkdScalar / UseRkdScalar
├── Features        one class per capability
├── Middleware      UI protection
├── OpenApi         security scheme transformers
└── Security        JWT, Basic, API Key, validators
```

---

# Compatibility

- .NET 10
- ASP.NET Core Minimal APIs
- ASP.NET Core Controllers

Every release is checked against the previously published package with
[package validation](https://learn.microsoft.com/dotnet/fundamentals/apicompat/package-validation/overview),
so public APIs are never removed or changed in a breaking way within a major version.

---

# Roadmap

- OAuth2 flows in the Scalar authentication panel
- Refresh tokens
- ProblemDetails responses for authentication failures

See the [CHANGELOG](https://github.com/rkdcoder/Rkd.Scalar/blob/master/CHANGELOG.md) for release history.

---

# Contributing

Pull requests are welcome.

Open an issue to propose new features or improvements.

---

# License

MIT License

---

# Author

Built on the belief that API documentation should take minutes, not hours.
