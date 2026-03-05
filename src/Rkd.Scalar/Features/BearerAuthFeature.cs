using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Rkd.Scalar.OpenApi;
using Rkd.Scalar.Security.Contracts;
using Rkd.Scalar.Security.Jwt;
using System.Text;

namespace Rkd.Scalar.Features
{
    internal sealed class BearerAuthFeature<TCredential, TValidator>
        : IScalarFeature, IBearerAuthFeature
        where TCredential : class
        where TValidator : class, ICredentialValidator<TCredential>
    {
        private readonly JwtOptions _options;

        public BearerAuthFeature(JwtOptions options)
        {
            _options = options;
        }

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            ValidateOptions(_options);

            services.AddScoped<ICredentialValidator<TCredential>, TValidator>();

            services.AddSingleton(_options);
            services.AddSingleton<IJwtTokenService, JwtTokenService>();

            var key = Encoding.UTF8.GetBytes(_options.Secret);

            services.AddAuthentication()
                .AddJwtBearer(jwt =>
                {
                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(key),

                        ValidateIssuer = true,
                        ValidIssuer = _options.Issuer,

                        ValidateAudience = true,
                        ValidAudience = _options.Audience,

                        ValidateLifetime = true,

                        ClockSkew = TimeSpan.Zero
                    };
                });

            services.ConfigureAll<OpenApiOptions>(options =>
            {
                options.AddDocumentTransformer<BearerSecurityTransformer>();
            });
        }

        public void ConfigureApp(WebApplication app)
        {
        }

        private static void ValidateOptions(JwtOptions options)
        {
            if (options.Secret.Length < 32)
                throw new InvalidOperationException("JWT secret must be at least 32 characters.");
        }
    }
}
