# Rkd.Scalar

<p align="center">
  <img src="https://raw.githubusercontent.com/rkdcoder/Rkd.Scalar/master/src/Rkd.Scalar/Media/icon.png" width="128" alt="Rkd.Scalar logo" />
</p>

[![NuGet](https://img.shields.io/nuget/v/Rkd.Scalar.svg)](https://www.nuget.org/packages/Rkd.Scalar)
[![Build & Publish](https://github.com/rkdcoder/Rkd.Scalar/actions/workflows/main.yml/badge.svg)](https://github.com/rkdcoder/Rkd.Scalar/actions/workflows/main.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**Rkd.Scalar** is a modern wrapper for integrating Scalar with ASP.NET APIs, focused on simplicity, security, and productivity.

It encapsulates all the configuration required for OpenAPI documentation, authentication (Basic and JWT Bearer), protection of the Scalar interface, and API versioning into a simple set of extensions.

The goal is to allow any ASP.NET project to configure complete documentation in just a few minutes, without needing to deal directly with the complexity of OpenAPI infrastructure.

---

## 🚀 Why use Rkd.Scalar?

- **Zero Boilerplate:** Reduce 50+ lines of configuration to just 5.
- **UI Protection:** [Key Feature] Protect your documentation page with a password (Basic Auth) without complicating your middleware pipeline.
- **JWT & Basic Auth:** Integrated and simplified security configuration for testing and internal services.
- **Versioning:** Native integration with `Asp.Versioning`.
- **Modern UI:** Uses [Scalar](https://scalar.com/) (the modern replacement for Swagger UI).

---

# Installation

Install via NuGet:

```
dotnet add package Rkd.Scalar
```

Or via Package Manager:

```
Install-Package Rkd.Scalar
```

---

# Basic Configuration

## Program.cs

Minimal configuration example:

```csharp
using Rkd.Scalar.Extensions;
using Rkd.Scalar.Security.Jwt;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddAuthorization();

var jwtOptions = new JwtOptions
{
    Secret = "SUPER_SECRET_KEY",
    Issuer = "MyApi",
    Audience = "MyApiClient",
    Expiration = TimeSpan.FromHours(2),
    ValidateNotBefore = true
};

builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithVersioning("v1", "v2", "v3")
    .WithUiProtection<UiDocCredentialValidator>()
    .WithBasicAuth<UiDocCredentialValidator>()
    .WithBearerAuth<AuthCredential, ApiCredentialValidator>(jwtOptions);

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

Or loading it from appsettings.json

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

---

# API Versioning

The package integrates with **Asp.Versioning**.

Example of a versioned controller:

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

In Scalar, each version will be displayed as a separate document.

---

# Basic Authentication

To enable Basic Auth:

```csharp
builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithBasicAuth<UiCredentialValidator>();
```

Validator implementation:

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
                new[] { new Claim(ClaimTypes.Name, request.Username), new Claim(ClaimTypes.Role, "ADMIN" ) },
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

To protect access to the documentation UI:

```csharp
builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithUiProtection<UiCredentialValidator>();
```

This requires Basic authentication to access the interface.

---

# JWT Authentication

Configuration in Program.cs:

```csharp
var jwtOptions = new JwtOptions
{
    Secret = "SUPER_SECRET_KEY",
    Issuer = "MyApi",
    Audience = "MyApiClient",
    Expiration = TimeSpan.FromHours(2),
    ValidateNotBefore = true
};

builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithBearerAuth<LoginRequest, LoginValidator>(jwtOptions);
```

Validator:

```csharp
public class LoginValidator : ICredentialValidator<LoginRequest>
{
    public Task<ClaimsIdentity?> ValidateAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Username == "admin" && request.Password == "123")
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, request.Username), new Claim(ClaimTypes.Role, "ADMIN" ) },
                "Bearer"
            );

            return Task.FromResult<ClaimsIdentity?>(identity);
        }

        return Task.FromResult<ClaimsIdentity?>(null);
    }
}
```

---

# Protected Endpoint

Example of a protected endpoint:

```csharp
[Authorize(AuthenticationSchemes = "Bearer,Basic")]
[HttpGet("secure")]
public IActionResult SecureEndpoint()
{
    return Ok("Authorized access");
}
```

---

# Launch in browser

To launch automatically in browser, modify the file YourProject/Properties/launchSettings.json in http and https:

Change launchBrowser to true:

```json
"launchBrowser": true
```

Add line "launchUrl": "scalar/v1" after "launchBrowser": true:

```json
"launchBrowser": true,
"launchUrl": "scalar/v1"
```

---

# Internal Library Structure

```
Rkd.Scalar

Configuration
Features
Security
Middleware
OpenApi
Extensions
Builder
```

The feature-based architecture allows new functionalities to be added easily without changing the core of the library.

---

# Advantages

- Extremely simple configuration
- Drastic reduction of boilerplate code
- Native integration with Scalar
- Extensible architecture
- Built-in security
- Ideal for microservices and enterprise APIs

---

# Compatibility

- .NET 10
- Modern ASP.NET Core

---

# Complete Example

```csharp
builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithVersioning("v1", "v2", "v3")
    .WithUiProtection<UiDocCredentialValidator>()
    .WithBasicAuth<UiDocCredentialValidator>()
    .WithBearerAuth<AuthCredential, ApiCredentialValidator>(jwtOptions);
```

---

# Roadmap

Planned features:

- OAuth2 support
- API Keys support
- documentation improvements

---

# Contribution

Pull requests are welcome.

Open an issue to discuss new features or improvements.

---

# License

MIT License

---

# Author

Project created to simplify the use of Scalar in modern ASP.NET APIs.
