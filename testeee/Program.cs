
using Rkd.Scalar.Configuration;
using Rkd.Scalar.Extensions;
using Rkd.Scalar.Security.Basic;
using Rkd.Scalar.Security.Jwt;
using testeee;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddAuthorization();

var jwtOptions = new JwtOptions
{
    Secret = "SUPER_SECRET_KEYSUPER_SECRET_KEYSUPER_SECRET_KEY",
    Issuer = "MyApi",
    Audience = "MyApiClient",
    Expiration = TimeSpan.FromHours(2),
    ValidateNotBefore = true
};

builder.Services
    .AddRkdScalar(builder.Configuration)
    .WithAutoVersioning()
    .WithUiProtection<UiCredentialValidator>()
    .WithBasicAuth<UiCredentialValidator>()
    .WithBearerAuth<BasicAuthCredentials, UiCredentialValidator>(jwtOptions);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.UseRkdScalar(new RkdScalarConfiguration
{
    Title = "My API"
});

app.Run();