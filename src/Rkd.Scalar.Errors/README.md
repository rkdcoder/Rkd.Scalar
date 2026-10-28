# Rkd.Scalar.Errors

The errors of [Rkd.Scalar](https://www.nuget.org/packages/Rkd.Scalar) for any layer — Domain, Application,
Infrastructure. Throw them anywhere; `WithProblemDetails()` in the API project turns them into RFC 9457 problem details
with a stable `code`.

```
dotnet add src/MyApp.Application package Rkd.Scalar.Errors
dotnet add src/MyApp.Api package Rkd.Scalar
```

```csharp
// MyApp.Application — references Rkd.Scalar.Errors only
public static class CustomerErrors
{
    public const string AlreadyExists = "CUSTOMER_ALREADY_EXISTS";
}

if (await customers.ExistsAsync(command.Document, cancellationToken))
    throw RkdError.Conflict(CustomerErrors.AlreadyExists, "A customer with this document already exists.")
        .WithLogDetails($"document={command.Document}");   // logged, never sent to the client

// MyApp.Api — references Rkd.Scalar
builder.AddRkdScalar().WithProblemDetails();
```

```json
{
  "title": "Conflict",
  "status": 409,
  "detail": "A customer with this document already exists.",
  "code": "CUSTOMER_ALREADY_EXISTS",
  "traceId": "00-…"
}
```

| Type | Use |
| --- | --- |
| `RkdError` | `Conflict`, `NotFound`, `UnprocessableEntity`, `Validation`… with a stable error code |
| `ProblemException` | The exception they create; derive from it for your own problems |
| `IProblemLogDetails` | Technical details of your own exceptions, written to the log only |

- Same types and namespace (`Rkd.Scalar`) used by Rkd.Scalar, which depends on this package.
- Dependencies: [Rkd.Problems](https://www.nuget.org/packages/Rkd.Problems) (the member names and codes shared with
  the clients) and the ASP.NET Core shared framework, needed by `ProblemException.ToProblemDetails()`. No other
  package: no Scalar, OpenAPI, API versioning or authentication.
- Validators, signers, key resolvers and HTTP log sinks implemented outside the API project are in
  [Rkd.Scalar.Abstractions](https://www.nuget.org/packages/Rkd.Scalar.Abstractions).

See the [Rkd.Scalar documentation](https://github.com/rkdcoder/Rkd.Scalar#layered-solutions-clean-architecture--ddd).
