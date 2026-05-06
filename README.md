# Rkd.Scalar - API Documentation Platform for ASP.NET

<p align="center">
  <img src="https://raw.githubusercontent.com/rkdcoder/Rkd.Scalar/master/src/Rkd.Scalar/Media/icon.png" width="128" alt="Rkd.Scalar logo" />
</p>

[![NuGet](https://img.shields.io/nuget/v/Rkd.Scalar.svg)](https://www.nuget.org/packages/Rkd.Scalar)
[![Build & Publish](https://github.com/rkdcoder/Rkd.Scalar/actions/workflows/main.yml/badge.svg)](https://github.com/rkdcoder/Rkd.Scalar/actions/workflows/main.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**Rkd.Scalar** adds a production-ready API documentation platform.
It combines documentation, authentication helpers and security protections into a single fluent configuration layer.

---

# Core Features

Rkd.Scalar integrates:

- **Scalar UI**
- **OpenAPI generation**
- **JWT Bearer authentication**
- **API Key authentication**
- **Basic authentication**
- **API versioning integration**
- **Scalar UI protection**
- **Default JWT login endpoint with built-in rate limiting**

All features are enabled through a simple **fluent builder API**.

---

# Why Rkd.Scalar

Rkd.Scalar focuses on three principles:

- **Simplicity** – drastically reduce OpenAPI setup code
- **Security** – protect documentation and test endpoints safely
- **Extensibility** – modular feature-based architecture

### Key Capabilities

- Minimal configuration
- Built-in **Basic Authentication** support
- Built-in **JWT Bearer Authentication** support
- Built-in **API Key Authentication** support
- Optional **default JWT login endpoint**
- **Scalar UI protection** via Basic Auth
- Built-in **API versioning integration**
- Feature-based modular architecture

---

# Who is this for?

- Teams building internal APIs
- SaaS platforms exposing partner APIs
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

Most APIs can enable Scalar with only a few lines:

```csharp
// JWT validation only (without login endpoint)
builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithVersioning("v1")
    .WithBearerAuth(jwtOptions);
```
```csharp
// JWT + default login endpoint
builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithVersioning("v1")
    .WithBearerAuth<AuthCredential, LoginValidator>(jwtOptions)
    .WithDefaultJwtLogin<AuthCredential>("/auth/login", 5, TimeSpan.FromMinutes(1));
```


Run the application and open:

```
/scalar/v1
```

You now have:

- OpenAPI documentation
- Scalar UI
- JWT authentication
- Secure login endpoint

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

Minimal setup example:

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

builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithVersioning("v1")
    .WithBearerAuth<AuthCredential, LoginValidator>(jwtOptions)
    .WithDefaultJwtLogin<AuthCredential>("/auth/login", 5, TimeSpan.FromMinutes(1));

var app = builder.Build();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.UseRkdScalar(new RkdScalarConfiguration
{
    Title = "My API"
});

app.Run();
```

---

# Customizing the Scalar UI

Rkd.Scalar allows full customization of Scalar through `ConfigureScalar`.

Simple UI configuration:

```csharp
app.UseRkdScalar(new RkdScalarConfiguration
{
    Title = "My API",
    Theme = ScalarTheme.BluePlanet
});
```

Advanced Scalar customization

```csharp
app.UseRkdScalar(new RkdScalarConfiguration
{
    Title = "My API",
    ConfigureScalar = opt =>
    {
        opt.DarkMode = true;
        opt.Theme = ScalarTheme.BluePlanet;
    }
});
```

This gives direct access to **ScalarOptions** while still keeping Rkd.Scalar's simplified configuration.

---

# Why not Swashbuckle?

| Feature                    | Swashbuckle | Rkd.Scalar |
| -------------------------- | ----------- | ---------- |
| **OpenAPI generation**     | ✔           | ✔          |
| **Scalar UI**              | ❌          | ✔          |
| **JWT login endpoint**     | ❌          | ✔          |
| **API Key auth**           | manual      | built-in   |
| **UI protection**          | ❌          | ✔          |
| **Versioning integration** | manual      | built-in   |

---

# Configuration via appsettings.json

Rkd.Scalar can also be configured using **appsettings.json**.

Example configuration:

```json
{
  "RkdScalar": {
    "Title": "My API",
    "OpenApiRoutePattern": "/openapi/{documentName}.json",
    "Theme": "BluePlanet"
  }
}
```

Program.cs:

```csharp
...
app.MapControllers();

var scalarOptions =
    builder.Configuration
           .GetSection("RkdScalar")
           .Get<RkdScalarConfiguration>()!;

scalarOptions.ConfigureScalar = opt =>
{
    opt.DarkMode = true;
};

app.UseRkdScalar(scalarOptions);

app.Run();
```

This approach is useful for:

- environment‑based configuration
- DevOps pipelines
- centralized configuration

---

# Default JWT Login Endpoint

Rkd.Scalar can automatically expose a login endpoint that issues JWT tokens.

```csharp
.WithBearerAuth<AuthCredential, LoginValidator>(jwtOptions)
.WithDefaultJwtLogin<AuthCredential>("/auth/login", 5, TimeSpan.FromMinutes(1))
```

This creates:

```
POST /auth/login
```

The request body is automatically bound to the credential model (TCredential).

This means the JSON payload must match the credential type configured in
WithBearerAuth<TCredential, TValidator>().

Example request:

```json
{
  "username": "admin",
  "password": "123"
}
```

Example response:

```json
{
  "access_token": "JWT_TOKEN",
  "expires_at": "2026-01-01T12:00:00Z"
}
```

---

# Built-in Brute Force Protection

The default login endpoint automatically configures **ASP.NET Rate Limiting**.

Example:

```csharp
.WithDefaultJwtLogin<AuthCredential>(
    "/auth/login",
    5,
    TimeSpan.FromMinutes(1))
```

This means:

- Maximum **5 login attempts**
- Within **1 minute**

If exceeded, the API returns:

```
HTTP 429 Too Many Requests
```

Don't forget to add `app.UseRateLimiter();` before `app.UseAuthentication();` in your program.cs file.

This protects the login endpoint against **brute-force attacks**.

### Important

`WithDefaultJwtLogin()` requires JWT authentication with credential validation.

You must configure:

```csharp
.WithBearerAuth<TCredential, TValidator>()
```

before calling it.

The overload below is validation-only and cannot issue login tokens:

```csharp
.WithBearerAuth(jwtOptions)
```

before calling it.

### Credential Type Requirement

The credential type used in `WithDefaultJwtLogin<TCredential>()` **must be the same** used in `WithBearerAuth<TCredential, TValidator>()`.

Correct usage:

```csharp
.WithBearerAuth<AuthCredential, LoginValidator>(jwtOptions)
.WithDefaultJwtLogin<AuthCredential>("/auth/login", 5, TimeSpan.FromMinutes(1))
```

Incorrect usage (will throw an exception during startup):

```csharp
.WithBearerAuth<BasicAuthCredentials, UiCredentialValidator>(jwtOptions)
.WithDefaultJwtLogin<AuthCredential>("/auth/login", 5, TimeSpan.FromMinutes(1))
```

The login endpoint depends on the credential model registered for JWT authentication, therefore both methods must use the **same request model type**.

# Authentications

**Important**

`ICredentialValidator<T>` is **provided by the Rkd.Scalar NuGet package** (`Rkd.Scalar.Security.Contracts`).

When implementing validators, you should **use the interface from the package**, not create your own interface with the same name.
This interface defines the contract used internally by Rkd.Scalar authentication features (Basic, JWT, and API Key).

---

## Basic Authentication

```csharp
builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithBasicAuth<UiCredentialValidator>();
```

Validator example:

```csharp
public class UiCredentialValidator : ICredentialValidator<BasicAuthCredentials>
{
    public Task<ClaimsIdentity?> ValidateAsync(
        BasicAuthCredentials request,
        CancellationToken cancellationToken = default)
    {
        if (request.Username == "admin" && request.Password == "123")
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, request.Username) },
                "Basic");

            return Task.FromResult<ClaimsIdentity?>(identity);
        }

        return Task.FromResult<ClaimsIdentity?>(null);
    }
}
```

---

# JWT Authentication

Rkd.Scalar supports two JWT modes:

## 1) Validation-only (no login endpoint)

Use this mode when your API only validates bearer tokens issued elsewhere.

```csharp
var jwtOptions = new JwtOptions
{
    Secret = "SUPER_SECRET_KEY_MINIMUM_32_CHARACTERS",
    Issuer = "MyApi",
    Audience = "MyApiClient",
    Expiration = TimeSpan.FromHours(2),
    ValidateNotBefore = true
};

builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithBearerAuth(jwtOptions);
```

This configures JWT validation and OpenAPI Bearer security without requiring
a credential model or validator.

## 2) JWT with default login endpoint

Use this mode when you want Rkd.Scalar to expose a login endpoint that issues tokens.

```csharp
builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithBearerAuth<AuthCredential, LoginValidator>(jwtOptions)
    .WithDefaultJwtLogin<AuthCredential>("/auth/login", 5, TimeSpan.FromMinutes(1));
```

Example credential model:

```csharp
public class AuthCredential
{
    public string Username { get; set; }
    public string Password { get; set; }
}
```

Validator example:

```csharp
using Rkd.Scalar.Security.Contracts;
using System.Security.Claims;

public class LoginValidator : ICredentialValidator<AuthCredential>
{
    public Task<ClaimsIdentity?> ValidateAsync(
        AuthCredential request,
        CancellationToken cancellationToken = default)
    {
        if (request.Username == "admin" && request.Password == "123")
        {
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, request.Username),
                    new Claim(ClaimTypes.Role, "ADMIN")
                },
                "Bearer"
            );

            return Task.FromResult<ClaimsIdentity?>(identity);
        }

        return Task.FromResult<ClaimsIdentity?>(null);
    }
}
```

---

# API Key Authentication

Rkd.Scalar supports **API Key authentication** using a request header.

Enable API Key support:

```csharp
builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithApiKeyAuth<ApiKeyValidator>();
```

Requests must include the header:

```
X-API-Key: YOUR_API_KEY
```

Validator example:

```csharp
using Rkd.Scalar.Security.ApiKey;
using Rkd.Scalar.Security.Contracts;
using System.Security.Claims;

public class ApiKeyValidator : ICredentialValidator<ApiKeyCredentials>
{
    public Task<ClaimsIdentity?> ValidateAsync(
        ApiKeyCredentials request,
        CancellationToken cancellationToken = default)
    {
        if (request.Key == "ABC123")
        {
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, "ApiKeyUser"),
                    new Claim(ClaimTypes.Role, "SERVICE")
                },
                "ApiKey"
            );

            return Task.FromResult<ClaimsIdentity?>(identity);
        }

        return Task.FromResult<ClaimsIdentity?>(null);
    }
}
```

The API Key scheme will automatically appear in the **Scalar authentication panel**.

---

# Securing Endpoints

Example protected endpoint:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Authorize(AuthenticationSchemes = "Bearer,Basic,ApiKey")]
[HttpGet("secure")]
public IActionResult SecureEndpoint()
{
    return Ok("Authorized access");
}
```

---

# Protecting the Scalar UI

To require authentication before accessing the documentation UI:

```csharp
builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithUiProtection<UiCredentialValidator>();
```

This protects:

- Scalar UI
- OpenAPI documents

using Basic Authentication.

---

# API Versioning

Rkd.Scalar integrates with **Asp.Versioning** automatically.

Configuration:

```csharp
builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithVersioning("v1", "v2", "v3");
```

Example controller:

```csharp
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments")]
public class PaymentController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok("API v1 running");
    }
}
```

Scalar automatically generates a document for each version.

---

# Launch Scalar Automatically

To open Scalar automatically when the application starts:

Edit:

```
Properties/launchSettings.json
```

Enable browser launch:

```json
"launchBrowser": true
```

Set Scalar as startup page:

```json
"launchBrowser": true,
"launchUrl": "scalar/v1"
```

---

# Feature‑Based Architecture

Rkd.Scalar is built using a modular **feature system**.

```
Rkd.Scalar

Builder
Configuration
Extensions
Features
Middleware
OpenApi
Security
```

Each capability (JWT, Basic Auth, API Key Auth, Versioning, UI protection) is implemented as an independent feature.

This makes the library:

- easy to extend
- easy to maintain
- easy to evolve

---

# Advantages

- Extremely simple configuration
- Minimal OpenAPI boilerplate
- Built-in security features
- Modern Scalar documentation UI
- Versioning support
- Highly extensible architecture

---

# Compatibility

- .NET 10
- ASP.NET Core Minimal APIs
- ASP.NET Core Controllers

---

# Production Example

```csharp
builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithVersioning("v1", "v2", "v3")
    .WithUiProtection<UiCredentialValidator>()
    .WithBasicAuth<UiCredentialValidator>()
    .WithBearerAuth<AuthCredential, LoginValidator>(jwtOptions)
    .WithDefaultJwtLogin<AuthCredential>("/auth/login", 5, TimeSpan.FromMinutes(1))
    .WithApiKeyAuth<ApiKeyValidator>()
    .WithLowercaseRouting();
```

If your API does not expose a login endpoint, prefer the simpler JWT setup:

```csharp
.WithBearerAuth(jwtOptions)
```

---

## Features Overview

Below is a brief explanation of each feature available in **Rkd.Scalar** and the problem it is designed to solve.

---

### API Versioning

```csharp
.WithVersioning("v1", "v2", "v3")
```

Enables API versioning and automatically exposes each version in the Scalar documentation UI.

Each version becomes selectable in the documentation interface, allowing developers to test and explore different API versions independently.

This feature integrates with **ASP.NET API Versioning** and configures the OpenAPI documents required for Scalar.

Typical use cases:

- Maintaining backward compatibility between API versions
- Gradually migrating clients between versions
- Supporting multiple client applications using different API versions

---

### Scalar UI Protection

```csharp
.WithUiProtection<UiCredentialValidator>()
```

Protects the **Scalar documentation interface** using Basic Authentication.

This prevents unauthorized users from accessing the API documentation while still allowing the API itself to remain public if desired.

The provided validator (`ICredentialValidator<BasicAuthCredentials>`) is responsible for validating the credentials used to access the UI.

Typical use cases:

- Restricting documentation access in production environments
- Allowing only internal teams to view API documentation
- Preventing accidental exposure of internal APIs

---

### Basic Authentication

```csharp
.WithBasicAuth<UiCredentialValidator>()
```

Enables **HTTP Basic Authentication** support for API endpoints.

This feature registers the required OpenAPI security scheme and integrates the authentication flow so credentials can be provided directly from the Scalar UI when testing endpoints.

The validator implementation is responsible for validating the provided username and password.

Typical use cases:

- Internal APIs
- Simple service-to-service authentication
- Legacy integrations

---

### JWT Bearer Authentication

Rkd.Scalar supports two JWT setup modes:

Validation-only (no login endpoint):

```csharp
.WithBearerAuth(jwtOptions)
```

JWT + login endpoint support:

```csharp
.WithBearerAuth<AuthCredential, LoginValidator>(jwtOptions)
```

The validation-only mode configures token validation and OpenAPI Bearer scheme.
The generic mode additionally enables credential validation flow required by
`WithDefaultJwtLogin<TCredential>()`.

Enables **JWT Bearer authentication** for the API.

This feature configures:

- JWT token validation
- OpenAPI security definitions
- Authentication middleware integration

The credential model (`AuthCredential`) represents the login payload, while the validator (`LoginValidator`) validates the credentials before issuing a token.

Typical use cases:

- Modern API authentication
- Stateless authentication
- Mobile and SPA clients

---

### Default JWT Login Endpoint

```csharp
.WithDefaultJwtLogin<AuthCredential>("/auth/login", 5, TimeSpan.FromMinutes(1))
```

Registers a **default login endpoint** that issues JWT access tokens.

The endpoint automatically binds the request body to the credential model (`TCredential`) and validates it using the configured `ICredentialValidator<TCredential>`.

Example endpoint:

```
POST /auth/login
```

The endpoint includes built-in **rate limiting** to protect against brute-force attacks.

Example configuration above means:

- Maximum **5 login attempts**
- Within **1 minute**

If the limit is exceeded, the API returns:

```
HTTP 429 Too Many Requests
```

Typical use cases:

- Development environments
- Rapid prototyping
- Simple authentication scenarios

If needed, a custom authentication endpoint can still be implemented manually.

---

### API Key Authentication

```csharp
.WithApiKeyAuth<ApiKeyValidator>()
```

Enables **API Key authentication** support.

Clients authenticate by sending an API key in the request header.

```
X-API-Key: YOUR_API_KEY
```

The provided validator (`ICredentialValidator<ApiKeyCredentials>`) is responsible for validating the API key.

The feature automatically registers the OpenAPI security scheme so the API key can be provided directly from the Scalar UI.

Typical use cases:

- Partner integrations
- Machine-to-machine communication
- Public APIs with controlled access

---

### Lowercase Routing

```csharp
.WithLowercaseRouting()
```

Configures ASP.NET routing to generate **lowercase URLs and query strings**.

This improves URL consistency and avoids issues caused by case-sensitive routing in certain environments.

Benefits include:

- Consistent API URLs
- Better compatibility with proxies and gateways
- Improved SEO for public APIs

---

# Roadmap

Planned features:

- OAuth2 support
- Extended Scalar customization

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
