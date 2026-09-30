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
  with **standard claim names** (`sub`, `name`, `role`…) and `iat` / `jti`, **keys resolved at runtime by `kid`**
  (database / key registry), `JwtBearerOptions` hooks and **service tokens for outgoing `HttpClient` calls**
- **Standardized errors (RFC 9457)** — every exception and error response becomes `application/problem+json`,
  with exception mapping, stable error **codes** (`RkdError.Conflict("CUSTOMER_ALREADY_EXISTS", …)`, `RkdResults`),
  error responses documented in Scalar and safe defaults (no internals leaked in production)
- **HTTP logging to any destination** — every request (status, duration, user, bodies, error `code`, exception)
  queued in memory and written in batches by a background service; SQL Server with `Rkd.Scalar.HttpLogging.SqlServer`
  or your own `IHttpLogSink`, credentials redacted, the API is never slowed down
- **API Key** and **Basic** authentication
- **Configuration-based validators** — protect the UI, Basic and API Key with **zero validator code**
- **Scalar UI protection**
- **One JSON setting for everything** — `WithJsonNaming(JsonNamingPolicy.SnakeCaseLower)` applies to controllers,
  minimal APIs, the OpenAPI schemas shown by Scalar and validation errors
- **API versioning integration** and **API modules** — `[ApiModule("billing")]` groups controllers by route and Scalar tag
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
    "VersionSelector": true,
    "DarkMode": true
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
| `DarkMode`            | Scalar default                  | Opens the UI in dark (`true`) or light (`false`) mode                        |

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

## File uploads

`IFormFile`, `IFormFileCollection` and `List<IFormFile>` — as parameters or inside `[FromForm]` models — are
documented as `{ "type": "string", "format": "binary" }`, so Scalar shows a file picker. ASP.NET Core 10 writes
them as `$ref: IFormFile`, which Scalar does not render; `[FromForm]` models with files are also offered as
`multipart/form-data` (MVC only declares `application/x-www-form-urlencoded`). Nothing to configure — remove your
own `FormFileSchemaTransformer` if you had one.

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
  "code": "ORDER_NOT_FOUND",
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
| `throw RkdError.Conflict("CODE", ...)` / `return RkdResults.NotFound("CODE", ...)` | The status with your stable `code`      |
| `Results.Problem`, `ValidationProblem`, `[ApiController]` model validation, `AddValidation()` | Enriched with `instance`, `traceId` and `code` (`VALIDATION_ERROR`) |
| Malformed JSON / bad request binding                     | `400` problem details — no .NET type names outside Development  |
| Client aborted the request                               | `499`, no error log                                             |

The response is always JSON (even for `Accept: text/html`), the `traceId` matches your logs and
distributed traces, and every problem has a machine-readable [`code`](#error-codes). Responses produced
from exceptions discard anything the failed request had written and are sent with `Cache-Control: no-store`;
for body-less responses, headers such as `WWW-Authenticate` are preserved.

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

## Error codes

Messages change and get translated; clients should branch on a **stable code**. Every problem carries a
`code` member:

| Problem                                   | `code`                                                     |
| ----------------------------------------- | ---------------------------------------------------------- |
| `RkdError` / `RkdResults` / `ProblemException { Code = ... }` | the code you chose (`CUSTOMER_ALREADY_EXISTS`) |
| Validation (`[ApiController]`, `AddValidation()`, `ValidationProblem`, `RkdError.Validation`) | `VALIDATION_ERROR` |
| Anything else                             | the status reason phrase: `NOT_FOUND`, `UNAUTHORIZED`, `FORBIDDEN`, `TOO_MANY_REQUESTS`, `INTERNAL_SERVER_ERROR`… |

Throw from anywhere (services, handlers, domain) with `RkdError`:

```csharp
throw RkdError.Conflict("CUSTOMER_ALREADY_EXISTS", "Já existe um cliente com este CPF.");
throw RkdError.NotFound("CUSTOMER_NOT_FOUND", $"Customer {id} was not found.");
throw RkdError.UnprocessableEntity("CREDIT_LIMIT_EXCEEDED", "The credit limit was exceeded.");
throw RkdError.Create(StatusCodes.Status402PaymentRequired, "PAYMENT_REQUIRED");
```

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Conflict",
  "status": 409,
  "detail": "Já existe um cliente com este CPF.",
  "instance": "/customers",
  "code": "CUSTOMER_ALREADY_EXISTS",
  "traceId": "00-…"
}
```

Keep the codes as constants (`public static class CustomerErrors { public const string AlreadyExists = "CUSTOMER_ALREADY_EXISTS"; }`)
so they can be shared with clients and tests. Codes accept letters, digits, `_`, `-` and `.`; invalid codes
fail when the error is created. Mapped exceptions can use the same shape:

```csharp
options.Map<DuplicatedDocumentException>(ex => RkdError.Conflict("CUSTOMER_ALREADY_EXISTS", ex.Message).ToProblemDetails());
```

Set `IncludeDefaultCodes = false` to send only the codes you set explicitly.

## Returning problems (RkdResults)

When the flow is expected (not found, conflict…), return instead of throwing. `RkdResults` has the same
methods as `RkdError` and works in **controllers and minimal APIs**:

```csharp
// controller — IActionResult or ActionResult<T>
[HttpGet("{id}")]
public async Task<ActionResult<Customer>> Get(int id) =>
    await _customers.FindAsync(id) is { } customer
        ? customer
        : RkdResults.NotFound("CUSTOMER_NOT_FOUND", $"Customer {id} was not found.");

// minimal API — IResult or Results<...>
app.MapGet("/customers/{id}", async Task<Results<Ok<Customer>, RkdProblemResult>> (int id, CustomerStore store) =>
    await store.FindAsync(id) is { } customer
        ? TypedResults.Ok(customer)
        : RkdResults.NotFound("CUSTOMER_NOT_FOUND"));
```

Success responses keep the standard `Ok`, `Created`, `NoContent`, `TypedResults` — there is no
`{ success, data }` envelope: HTTP status codes already say whether it worked, and wrappers break OpenAPI
schemas and generated clients. `RkdResults` works even without `WithProblemDetails()`; with it, the response
also gets `instance`, `traceId` and your `Customize`.

## Validation

Validation is ASP.NET Core's, standardized by Rkd.Scalar — every validation error has the same shape
(`errors` by field, `code: VALIDATION_ERROR`, `traceId`) whatever produced it:

- **Controllers** — `[ApiController]` validates DataAnnotations automatically.
- **Minimal APIs (.NET 10)** — call `builder.Services.AddValidation();` in your application. It is a source
  generator that only runs in the project that calls it, so a library cannot call it for you.
- **Your own rules (FluentValidation, RuleWeaver, custom)** — run them and throw or return the result:

```csharp
var errors = validator.Validate(request);   // any library
if (errors.Count > 0)
    throw RkdError.Validation(errors);      // IDictionary<string, string[]>

// or, in an endpoint
return RkdResults.Validation(errors);
```

Or map the library's exception once:

```csharp
options.Map<ValidationException>(ex => RkdError.Validation(
    ex.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
    .ToProblemDetails());
```

Outside Development, JSON conversion errors no longer expose .NET types
(`"The JSON value could not be converted to System.Int32…"` becomes `"The input was not valid."`).

## Errors in the OpenAPI document

Scalar shows the error responses of every operation, with the `application/problem+json` schema (including
`code` and `traceId`). Only what is certain is added — nothing is guessed:

| Response | Added when                                                                      |
| -------- | ------------------------------------------------------------------------------- |
| `400`    | the operation has parameters or a body (`HttpValidationProblemDetails` schema)  |
| `401`, `403` | the operation requires authorization (and is not `[AllowAnonymous]`)        |
| `429`    | the operation has rate limiting (`[EnableRateLimiting]` / `RequireRateLimiting`) |
| `500`    | always                                                                          |

Business errors (`404`, `409`, `422`…) depend on your code, so declare them as usual —
`[ProducesResponseType(StatusCodes.Status404NotFound)]`, `.ProducesProblem(404)` or
`/// <response code="404">` — and they get the problem schema as `application/problem+json`. Responses you
declare are never overwritten. Disable with `DocumentErrorResponses = false`.

## Throwing problems directly

```csharp
throw ProblemException.NotFound($"Order {id} was not found.");

throw new ProblemException(StatusCodes.Status409Conflict, "Duplicated order", $"Order {id} already exists.")
{
    Type = "https://errors.mycompany.com/duplicated-order",
    Code = "DUPLICATED_ORDER",
    Extensions = { ["orderId"] = id }
};
```

## Log-only details

Technical details (ids, SQL errors, upstream responses) belong in the log, never in the response:

```csharp
throw RkdError.Conflict("CUSTOMER_ALREADY_EXISTS", "Já existe um cliente com este CPF.")
    .WithLogDetails($"tenant={tenant} customerId={existing.Id}");

throw new ProblemException(502, detail: "Vector store unavailable.", innerException: ex) { LogDetails = $"collection={name}" };
```

Existing exceptions only need the `IProblemLogDetails` interface — no base class change, no extra mapping:

```csharp
public class AppException : Exception, IProblemLogDetails
{
    public string? Details { get; init; }          // "never goes to the client, only to the log"
    string? IProblemLogDetails.LogDetails => Details;
}
```

The details are appended to the log entry of the response (4xx as `Information`, 5xx as `Error`). For 4xx, the
exception is attached to the log entry (with its stack trace) when it has an `InnerException`, so the real cause
is not lost.

## Authentication failures

A token rejected with a `ProblemException` explains itself in the 401 body — for example a token from another
environment, checked in `OnTokenValidated` (see [ConfigureJwtBearer](#customizing-jwtbearer)):

```csharp
context.Fail(RkdError.Unauthorized("WRONG_ENVIRONMENT", "The token was issued for another environment."));
```

```json
{ "status": 401, "title": "Unauthorized", "detail": "The token was issued for another environment.", "code": "WRONG_ENVIRONMENT", "traceId": "…" }
```

Any other failure (expired, invalid signature…) keeps the generic `401` with `code: UNAUTHORIZED`: framework
messages are technical and are not sent. The `WWW-Authenticate` header is kept.

## Body-less responses (MapStatus)

Give `detail` and `code` to the responses ASP.NET Core produces without a body (unknown route, 405, 415, 401/403
from authorization):

```csharp
.WithProblemDetails(options => options
    .MapStatus(404, "ROUTE_NOT_FOUND", "Check the route and the API version (e.g. /api/v1/customers).")
    .MapStatus(405, "METHOD_NOT_ALLOWED", "This route does not accept this HTTP method.")
    .MapStatus(403, context => new ProblemDetails
    {
        Detail = $"Your profile cannot access {context.Request.Path}.",
        Extensions = { ["code"] = "ACCESS_DENIED" }
    }));
```

Responses written by your code, exceptions and `ProblemException` authentication failures are not affected.

## Upload limits (413)

Exceeding `FormOptions.MultipartBodyLengthLimit` (or `ValueCountLimit`, `ValueLengthLimit`…) returns
`413 Payload Too Large` with `code: PAYLOAD_TOO_LARGE` — whether the form is read by MVC model binding
(`IFormFile` in controllers), by `Request.ReadFormAsync()` or, in Development, by minimal API binding —
instead of a `500` or a validation error. A malformed form is a `400`. Outside Development, minimal API parameter
binding answers `400`, because ASP.NET Core does not tell why. `InvalidDataException`s thrown by your own code
are still `500`.

## Options

| Option                    | Default               | Description                                                              |
| ------------------------- | --------------------- | ------------------------------------------------------------------------ |
| `IncludeExceptionDetails` | Development only      | Adds `detail` and an `exception` object (type, message, stack trace) to unexpected errors |
| `HandleStatusCodes`       | `true`                | Converts body-less 4xx/5xx responses; opt out per endpoint with `[SkipStatusCodePages]` |
| `IncludeInstance`         | `true`                | Sets `instance` to the request path                                      |
| `IncludeDefaultCodes`     | `true`                | Adds `code` to problems without one (`VALIDATION_ERROR`, `NOT_FOUND`…)   |
| `DocumentErrorResponses`  | `true`                | Documents 400/401/403/429/500 problem responses in OpenAPI / Scalar      |
| `Customize`               | —                     | Adds or changes members of every problem (e.g. `service`, `tenant`)      |
| `UnexpectedError(code, detail, title)` | —        | `code`, `detail` and `title` of the `500` for unexpected exceptions      |

```csharp
.WithProblemDetails(options =>
{
    options.Customize = context =>
        context.ProblemDetails.Extensions["service"] = "orders-api";
});
```

Unexpected errors in the language of your clients, without mapping `Exception`:

```csharp
.WithProblemDetails(options => options.UnexpectedError(
    code: "ERRO_INESPERADO",
    detail: "Ocorreu um erro inesperado. Informe o traceId ao suporte.",
    title: "Erro inesperado"));
```

The exception message is never sent (in Development, `IncludeExceptionDetails` still adds the `exception` member)
and the error is still logged as `Error`.

Logging: unexpected errors (5xx) are logged as `Error` with the exception; mapped client errors (4xx)
as `Information`; aborted requests as `Debug`. The middleware is placed at the beginning of the pipeline
automatically, so it also catches errors from other middlewares — no `UseExceptionHandler` needed. If
you add your own `app.UseExceptionHandler(...)`, yours runs first for the requests it handles.
In Development, ASP.NET Core's developer exception page still writes its own error log line, but the
response is the same problem details as in production (plus the exception details).

---

# HTTP Logging

Every request can be logged to a database — or anywhere else — without slowing down the API:

```
request ──► capture (outermost middleware) ──► bounded in-memory queue ──► background writer ──► sinks (batches)
             never waits, never throws           full? entry dropped         SQL Server, your own IHttpLogSink…
```

```csharp
// dotnet add package Rkd.Scalar.HttpLogging.SqlServer
builder.AddRkdScalar()
    .WithHttpLogging(o => o.ExcludedPaths.Add("/health"))
    .WriteHttpLogsToSqlServer(builder.Configuration.GetConnectionString("Logs")!, "logs.HttpRequests", createTable: true);
```

Or entirely from configuration — `builder.AddRkdScalar().WriteHttpLogsToSqlServer();`:

```json
{
  "ConnectionStrings": { "Logs": "Server=...;Database=...;..." },
  "HttpLogging": {
    "MaxBodyBytes": 32768,
    "ExcludedPaths": [ "/health" ],
    "SensitivePaths": [ "/api/v1/users/password" ],
    "UserNameClaimTypes": [ "cpf" ],
    "SqlServer": { "ConnectionStringName": "Logs", "Table": "logs.HttpRequests", "CreateTable": true }
  }
}
```

## What each entry has

`TraceId` (the same `traceId` of the problem details responses), `StartedAt` (UTC), `Duration`, `Method`, `Path`,
`QueryString`, `RoutePattern` (`api/v{version}/orders/{id}` — group by endpoint), `StatusCode`, `ErrorCode` (the
problem `code`, e.g. `CUSTOMER_NOT_FOUND`), `Exception` (unhandled 5xx only), `UserName`, `UserId`, `ClientIp`,
`UserAgent`, `RequestHeaders`, request and response bodies, `ResponseSize`, `Properties` (your own values) and more.

## Safe and cheap by design

- **Never blocks**: the queue has a fixed capacity (`QueueCapacity`, 10,000); when the sinks cannot keep up, new
  entries are dropped and a warning is logged once a minute. A failing sink is logged (once a minute) and skipped.
- **No extra buffering**: bodies are captured while the application reads and writes them, up to `MaxBodyBytes`
  (32 KB). Streaming responses (server-sent events, downloads) keep streaming; binary and multipart bodies are not
  captured (only their size).
- **Batches**: sinks receive up to `BatchSize` (100) entries per call — the SQL Server sink writes each batch with
  one `SqlBulkCopy`.
- **Shutdown**: queued entries are written when the application stops (`ShutdownTimeout`, 5 s).
- **Redaction**: `Authorization` keeps only its scheme (`Bearer [REDACTED]`); `Cookie`, `X-API-Key` and the
  `WithApiKeyAuth` header are redacted, as the query parameters `access_token`, `token`, `api_key`, `password`…
  Bodies of `SensitivePaths` and of the `WithJwtLoginEndpoint` route are never stored.
- **Scalar UI and OpenAPI documents** are not logged (`ExcludeDocumentation`).

## Sensitive endpoints

Routes a path prefix cannot describe (`POST api/v1/systems/{id}/keys`, which returns a key in clear text) are marked
on the endpoint — the entry is kept, the bodies are never stored:

```csharp
[HttpPost("{id}/keys"), SensitiveHttpLog]          // controller or action
public ApiKeyCreated CreateKey(int id) => ...;

app.MapPost("/systems/{id}/keys", CreateKey).WithSensitiveHttpLog();   // minimal API (or a MapGroup)
```

The endpoint is known only after routing, so the captured bodies are discarded before the entry is queued: they
never reach a sink.

## Options

| Option                    | Default     | Description                                                            |
| ------------------------- | ----------- | ---------------------------------------------------------------------- |
| `Enabled`                 | `true`      | Turns the capture off (e.g. per environment)                           |
| `ExcludedPaths`           | —           | Not logged (by segment: `/health` excludes `/health/ready`)             |
| `SensitivePaths`          | login route | Bodies replaced by `[REDACTED]`                                        |
| `CaptureRequestBody` / `CaptureResponseBody` / `CaptureRequestHeaders` | `true` | What is captured      |
| `MaxBodyBytes`            | `32768`     | Bytes kept of each body (`RequestBodyTruncated` / `ResponseBodyTruncated`) |
| `RedactedHeaders` / `RedactedQueryParameters` | see above | Values replaced by `[REDACTED]`                        |
| `UserNameClaimTypes`      | `ClaimTypes.Name`, `name`, `preferred_username`, `unique_name`, email | First claim found becomes `UserName` |
| `UserIdClaimTypes`        | `ClaimTypes.NameIdentifier`, `sub`, `oid` | First claim found becomes `UserId`       |
| `QueueCapacity` / `BatchSize` / `ShutdownTimeout` | `10000` / `100` / `5 s` | Queue and writer                      |
| `ApplicationName`         | host name   | Written to every entry                                                 |
| `Filter`                  | —           | `context => bool`, after the response (e.g. only errors)               |
| `Enrich`                  | —           | `(context, entry) => entry.Properties["tenant"] = ...`                 |

```csharp
.WithHttpLogging(o =>
{
    o.UserNameClaimTypes = ["cpf"];                                   // users identified by CPF
    o.Filter = context => context.Response.StatusCode >= 400;         // only errors
    o.Enrich = (context, entry) => entry.Properties["tenant"] = context.User.FindFirst("tenant")?.Value;
})
```

## Your own destination

```csharp
public sealed class ElasticHttpLogSink(ElasticClient client) : IHttpLogSink
{
    public async Task WriteAsync(IReadOnlyList<HttpLogEntry> entries, CancellationToken cancellationToken) =>
        await client.BulkAsync(entries, cancellationToken);
}

builder.AddRkdScalar().WithHttpLogSink<ElasticHttpLogSink>();   // several sinks can be combined
```

Sinks are singletons called by the background writer — never by a request. Without any sink the application fails
at startup, so a missing destination is never silent.

## SQL Server

`Rkd.Scalar.HttpLogging.SqlServer` writes each batch with `SqlBulkCopy`. `CreateTable = true` creates the schema and
the table (indexed by `StartedAt` and `TraceId`) on the first write; without DDL permissions, run
`SqlServerHttpLogTable.CreateScript("logs.HttpRequests")` once. Only the columns your table has are written, so you
can drop the ones you don't want. Oversized values are cut to the column size, so one request never rejects a batch.

---

# JSON Naming and Settings

ASP.NET Core keeps **two independent JSON settings**: `AddJsonOptions` (controllers) and
`ConfigureHttpJsonOptions` (minimal APIs **and the OpenAPI schemas Scalar displays and sends**).
Configuring only one of them makes the documentation disagree with the real payloads. Rkd.Scalar configures
both at once:

```csharp
builder.AddRkdScalar()
    .WithJsonNaming(JsonNamingPolicy.SnakeCaseLower);   // unit_price, created_at, …
```

`WithJsonNaming` applies the policy to:

- controller and minimal API responses and requests;
- the OpenAPI schemas (what Scalar shows and uses in "Test Request");
- dictionary keys (`applyToDictionaryKeys: false` to keep them as they are);
- model validation errors — `"errors": { "unit_price": [...] }` instead of `UnitPrice`.

Any other serializer setting goes through `ConfigureJson`, also applied to both:

```csharp
builder.AddRkdScalar()
    .WithJsonNaming(JsonNamingPolicy.SnakeCaseLower)
    .ConfigureJson(json =>
    {
        json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));   // "on_hold"
        json.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
```

Problem details members (`type`, `title`, `status`, `detail`, `instance`, `code`, `traceId`) and the login response
(`access_token`, `expires_in`…) keep their standard names regardless of the policy.

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

## API modules

Group controllers into modules with one attribute: standardized route, one Scalar/OpenAPI tag per module
and `[ApiController]` behavior included.

```csharp
[ApiModule("billing")]          // api/v1/billing/invoices — tag "billing"
[ApiVersion("1.0")]
public class InvoicesController : ControllerBase { ... }

[ApiModule("billing")]          // api/v1/billing/payments — same "billing" group in Scalar
[ApiVersion("1.0")]
public class PaymentsController : ControllerBase { ... }
```

| Template source                                                  | Example                                          |
| ---------------------------------------------------------------- | ------------------------------------------------ |
| Default with `WithVersioning`                                    | `api/v{version:apiVersion}/[module]/[controller]` |
| Default without versioning                                       | `api/[module]/[controller]`                      |
| Global: `.WithApiModules(o => o.RouteTemplate = "...")`          | `"v{version:apiVersion}/[module]/[controller]"`  |
| Per controller: `[ApiModule("x", RouteTemplate = "...")]`        | `"internal/[module]/[controller]"`               |

The template must contain `[module]`; `[controller]` and route parameters work as usual. Other options:
`Tag` (display name in Scalar, e.g. `[ApiModule("billing", Tag = "Billing & Payments")]`), `Order` and `Name`.
Module names may have several segments (`"finance/reports"`) and route parameters:

```csharp
[ApiModule("tools/{toolId}")]   // api/tools/{toolId}/attributes — tag "tools"
public class AttributesController : ControllerBase
{
    [HttpGet]
    public IActionResult Get(int toolId) => ...;
}
```

Parameters (`{toolId}`, `{toolId:int}`) are left out of the default tag; optional and catch-all parameters are
not allowed. Don't combine it with a class-level `[Route]`.

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

## Customizing JwtBearer

`ConfigureJwtBearer` gives access to ASP.NET Core's `JwtBearerOptions` — events, extra validation parameters —
without registering `AddJwtBearer` yourself (so the Bearer scheme keeps showing in Scalar):

```csharp
builder.AddRkdScalar()
    .WithBearerAuth()
    .ConfigureJwtBearer(jwt =>
    {
        jwt.Events.OnTokenValidated = context =>
        {
            if (context.Principal!.FindFirst("env")?.Value != builder.Environment.EnvironmentName)
                context.Fail(RkdError.Unauthorized("WRONG_ENVIRONMENT", "The token was issued for another environment."));

            return Task.CompletedTask;
        };

        // SignalR: token in the query string
        jwt.Events.OnMessageReceived = context =>
        {
            if (context.Request.Path.StartsWithSegments("/hubs"))
                context.Token = context.Request.Query["access_token"];
            return Task.CompletedTask;
        };
    });
```

It runs after Rkd.Scalar's settings, whatever the order of the calls. Failing with a `ProblemException` puts its
`code` and `detail` in the 401 response (see [Authentication failures](#authentication-failures)).

## Keys from a database (IJwtSigningKeyResolver)

When the validation keys live in a database or a key registry (one key per service, rotated at runtime), resolve
them by `kid`:

```csharp
public sealed class ServiceKeyResolver(ServiceKeysRepository keys) : IJwtSigningKeyResolver
{
    public async Task<IEnumerable<JwtSigningKey>> ResolveAsync(JwtSigningKeyContext context, CancellationToken cancellationToken)
    {
        var key = await keys.FindByKidAsync(context.KeyId, cancellationToken);   // context.Issuer is NOT verified yet

        return key is null
            ? []
            : [new JwtSigningKey(ECDsaKey(key.PublicKeyPem, key.Kid), issuer: key.ServiceName)];
    }
}

builder.AddRkdScalar()
    .WithJwtSigningKeyResolver<ServiceKeyResolver>()   // before WithBearerAuth when it is the only key source
    .WithBearerAuth();                                 // "Jwt": { "Audience": "data-api" } — Issuer optional
```

- **Async**: the lookup can query a database (the resolver is scoped, so it may use a `DbContext`).
- **Cached** by `kid` for `KeyCacheDuration` (10 min); returning every active key at once also works — each is
  cached by its own `kid`.
- **Rotation**: a token with an unknown `kid` triggers a new lookup before being validated. The same unknown `kid`
  is not looked up again for `UnknownKeyCacheDuration` (30 s), protecting the store from random `kid`s.
- **Issuer binding**: a key with `issuer` only validates tokens whose `iss` is that issuer, so a service cannot
  sign on behalf of another one. `Jwt:Issuer` may be omitted only when the resolver is the only key source; then
  every resolved key must have an issuer.
- Keys configured in `JwtOptions` keep working; a failing resolver rejects the token (401) and logs the error.

```csharp
.WithJwtSigningKeyResolver<ServiceKeyResolver>(o =>
{
    o.KeyCacheDuration = TimeSpan.FromMinutes(30);
    o.UnknownKeyCacheDuration = TimeSpan.FromSeconds(10);
})
```

## Calling other services (AddRkdJwtToken)

Authenticates outgoing `HttpClient` calls with a token issued by `IJwtTokenService` (HMAC, RSA/ECDSA or an async
`IJwtSigner` such as a KMS). The token is cached per client and renewed shortly before it expires:

```csharp
builder.AddRkdScalar().WithBearerAuth();   // a signing key: Secret, PrivateKeyPath or WithJwtSigner<T>()

builder.Services.AddHttpClient<VectorStoreClient>(c => c.BaseAddress = new Uri(vectorStoreUrl))
    .AddRkdJwtToken(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "ia-core")]),
        o => o.AdditionalClaims.Add(new Claim("scope", "vectors.write")));

// or build the identity from services / configuration
    .AddRkdJwtToken(sp => new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, sp.GetRequiredService<IConfiguration>()["ServiceName"]!)]));
```

Requests that already have an `Authorization` header are sent unchanged. `RefreshBeforeExpiration` (default 1 min,
never more than half the lifetime) controls the renewal.

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
| `ConfigureJwtBearer(configure)`                                     | `JwtBearerOptions` events and settings, without `AddJwtBearer` |
| `WithJwtSigningKeyResolver<TResolver>(configure?)`                  | Validation keys resolved at runtime by `kid` (database, registry) |
| `WithBasicAuth<TValidator>()` / `WithBasicAuth(section)`            | Basic authentication for the API                               |
| `WithApiKeyAuth<TValidator>(configure?)` / `WithApiKeyAuth(section, configure?)` | API Key authentication                            |
| `WithDefaultAuthenticationScheme()`                                 | Plain `[Authorize]` accepts every enabled scheme               |
| `WithOperationSecurity()`                                           | Security requirements only on protected operations            |
| `WithProblemDetails(configure?)`                                    | RFC 9457 errors for exceptions and error responses             |
| `WithApiModules(configure)`                                         | Global route template of `[ApiModule]` controllers             |
| `WithJsonNaming(policy)`                                            | Same JSON naming in controllers, minimal APIs, OpenAPI and validation errors |
| `ConfigureJson(configure)`                                          | Any JSON setting, applied to controllers and minimal APIs/OpenAPI |
| `WithHttpLogging(configure?)`                                       | HTTP request logging (queue + background writer)               |
| `WithHttpLogSink<TSink>()` / `WithHttpLogSink(factory)`             | Destination of the HTTP logs                                   |
| `WriteHttpLogsToSqlServer(...)`                                     | SQL Server destination (`Rkd.Scalar.HttpLogging.SqlServer`)    |
| `WithLowercaseRouting()`                                            | Lowercase URLs and query strings                               |

`IHttpClientBuilder.AddRkdJwtToken(identity)` authenticates outgoing calls with Rkd.Scalar tokens.

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
