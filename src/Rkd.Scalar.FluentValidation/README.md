# Rkd.Scalar.FluentValidation

[FluentValidation](https://docs.fluentvalidation.net) for [Rkd.Scalar](https://www.nuget.org/packages/Rkd.Scalar):
invalid requests answer with the standard Rkd.Scalar validation problem — no validation filter of your own.

```csharp
builder.AddRkdScalar()
    .WithJsonNaming(JsonNamingPolicy.SnakeCaseLower)
    .WithProblemDetails()
    .WithFluentValidation(typeof(Program).Assembly);   // registers the validators of the assembly

app.MapGroup("/api/orders").WithFluentValidation();     // minimal APIs: per endpoint or group
```

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "customer.zip_code": [ "'Zip Code' must not be empty." ] },
  "code": "VALIDATION_ERROR",
  "traceId": "00-…"
}
```

- **Controllers**: every action argument with a registered `IValidator<T>` is validated before the action runs,
  after the `[ApiController]` model state check (binding and DataAnnotations errors keep answering first).
- **Minimal APIs**: `.WithFluentValidation()` on endpoints or `MapGroup`s.
- Field names follow the JSON naming policy of controllers / minimal APIs (`Items[0].UnitPrice` → `items[0].unit_price`).
- Async rules (`MustAsync`) are supported; validators may use scoped services.
