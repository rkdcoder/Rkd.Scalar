# Rkd.Scalar.Abstractions

The contracts of [Rkd.Scalar](https://www.nuget.org/packages/Rkd.Scalar) for the layers that implement them —
Infrastructure, Application — **without ASP.NET Core**, Scalar, OpenAPI or API versioning. Install the whole
Rkd.Scalar only in the API project, which registers what the other layers implement.

```
dotnet add src/MyApp.Infrastructure package Rkd.Scalar.Abstractions
dotnet add src/MyApp.Api package Rkd.Scalar
```

```csharp
// MyApp.Infrastructure — references Rkd.Scalar.Abstractions only
public sealed class LoginValidator(AppDbContext db, IPasswordHasher hasher) : ICredentialValidator<LoginRequest>
{
    public async Task<CredentialValidationResult> ValidateAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == request.Username, cancellationToken);

        if (user is null || !hasher.Verify(request.Password, user.PasswordHash))
            return CredentialValidationResult.Failure("Invalid username or password.");

        return CredentialValidationResult.Success(
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username));
    }
}

// MyApp.Api — references Rkd.Scalar
builder.AddRkdScalar()
    .WithBearerAuth<LoginRequest, LoginValidator>();
```

| Area | Types |
| --- | --- |
| Credentials | `ICredentialValidator<TCredentials>`, `CredentialValidationResult`, `BasicAuthCredentials`, `ApiKeyCredentials`, `RkdScalarAuthenticationSchemes` |
| JWT | `IJwtTokenService`, `JwtToken`, `JwtLoginResponse`, `IJwtSigner` (KMS, Key Vault, HSM), `IJwtSigningKeyResolver`, `JwtSigningKey`, `JwtSigningKeyContext`, `RkdPem` |
| HTTP logging | `IHttpLogSink`, `HttpLogEntry` |

- Same types and namespace (`Rkd.Scalar`) used by Rkd.Scalar: nothing to map between layers. Rkd.Scalar depends on
  this package, so the API project gets it automatically.
- Only dependency: `Microsoft.IdentityModel.Tokens` (the `SecurityKey` of `JwtSigningKey`).
- Errors thrown from any layer (`RkdError`, `ProblemException`) are in
  [Rkd.Scalar.Errors](https://www.nuget.org/packages/Rkd.Scalar.Errors).

See the [Rkd.Scalar documentation](https://github.com/rkdcoder/Rkd.Scalar#layered-solutions-clean-architecture--ddd).
