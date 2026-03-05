# Rkd.Scalar

<p align="center">
  <img src="https://raw.githubusercontent.com/rkdcoder/Rkd.Scalar/master/src/Rkd.Scalar/Media/icon.png" width="128" alt="Rkd.Scalar logo" />
</p>

[![NuGet](https://img.shields.io/nuget/v/Rkd.Scalar.svg)](https://www.nuget.org/packages/Rkd.Scalar)
[![Build & Publish](https://github.com/rkdcoder/Rkd.Scalar/actions/workflows/main.yml/badge.svg)](https://github.com/rkdcoder/Rkd.Scalar/actions/workflows/main.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**Rkd.Scalar** is a modern wrapper that simplifies integrating **Scalar** into ASP.NET APIs.

It abstracts the complexity of configuring **OpenAPI**, **authentication**, **documentation UI protection**, and **API versioning** into a small and intuitive fluent configuration.

The goal is simple: configure professional API documentation in **minutes instead of hours**.

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
builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithVersioning("v1")
    .WithBearerAuth<AuthCredential, LoginValidator>(jwtOptions)
    .WithDefaultJwtLogin<AuthCredential>();
```

Run the application and open:

```
/scalar/v1
```

You now have:

- OpenAPI documentation
- Scalar UI
- JWT authentication
- Login endpoint

---

# Quick Start

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
    .WithDefaultJwtLogin<AuthCredential>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.UseRkdScalar(new RkdScalarConfiguration
{
    Title = "My API"
});

app.Run();
```

Your documentation will be available at:

```
/scalar/v1
```

---

# Configuration via appsettings.json

Rkd.Scalar can also be configured using **appsettings.json**.

Example configuration:

```json
{
  "RkdScalar": {
    "Title": "My API",
    "OpenApiRoutePattern": "/openapi/{documentName}.json"
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

app.UseRkdScalar(scalarOptions);

app.Run();
```

This approach is useful for:

- environment‑based configuration
- DevOps pipelines
- centralized configuration

---

# Default JWT Login Endpoint

Rkd.Scalar can automatically generate a **login endpoint** for JWT authentication.

This feature is **explicit and opt‑in**.

```csharp
.WithBearerAuth<AuthCredential, LoginValidator>(jwtOptions)
.WithDefaultJwtLogin<AuthCredential>()
```

This will create the endpoint:

```
POST /default-auth/login
```

Request body:

```json
{
  "username": "admin",
  "password": "123"
}
```

Response:

```json
{
  "access_token": "JWT_TOKEN",
  "expires_at": "2026-01-01T12:00:00Z"
}
```

### Custom Login Route

You can customize the endpoint path:

```csharp
.WithDefaultJwtLogin<AuthCredential>("/auth/login")
```

Generated endpoint:

```
POST /auth/login
```

### Important

`WithDefaultJwtLogin()` requires JWT authentication.

You must configure:

```csharp
.WithBearerAuth<TCredential, TValidator>()
```

before calling it.

### Credential Type Requirement

The credential type used in `WithDefaultJwtLogin<TCredential>()` **must be the same** used in `WithBearerAuth<TCredential, TValidator>()`.

Correct usage:

```csharp
.WithBearerAuth<AuthCredential, LoginValidator>(jwtOptions)
.WithDefaultJwtLogin<AuthCredential>()
```

Incorrect usage (will throw an exception during startup):

```csharp
.WithBearerAuth<BasicAuthCredentials, UiCredentialValidator>(jwtOptions)
.WithDefaultJwtLogin<AuthCredential>()
```

The login endpoint depends on the credential model registered for JWT authentication, therefore both methods must use the **same request model type**.

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

# Basic Authentication

Enable Basic Authentication support:

```csharp
builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithBasicAuth<UiCredentialValidator>();
```

Example validator:

```csharp
using Rkd.Scalar.Security.Basic;
using Rkd.Scalar.Security.Contracts;
using System.Security.Claims;

public class UiCredentialValidator : ICredentialValidator<BasicAuthCredentials>
{
    public Task<ClaimsIdentity?> ValidateAsync(
        BasicAuthCredentials request,
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
                "Basic"
            );

            return Task.FromResult<ClaimsIdentity?>(identity);
        }

        return Task.FromResult<ClaimsIdentity?>(null);
    }
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

# JWT Authentication

Example credential model:

```csharp
public class AuthCredential
{
    public string Username { get; set; }

    public string Password { get; set; }
}
```

This class can be **any model you prefer**. Rkd.Scalar only requires that the model contains the credentials needed by your validator.

Configuration:

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
    .WithBearerAuth<AuthCredential, LoginValidator>(jwtOptions);
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

# Example Configuration

```csharp
builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithVersioning("v1", "v2", "v3")
    .WithUiProtection<UiCredentialValidator>()
    .WithBasicAuth<UiCredentialValidator>()
    .WithBearerAuth<AuthCredential, LoginValidator>(jwtOptions)
    .WithDefaultJwtLogin<AuthCredential>()
    .WithApiKeyAuth<ApiKeyValidator>();
```

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

Created to simplify Scalar integration in modern ASP.NET APIs.
