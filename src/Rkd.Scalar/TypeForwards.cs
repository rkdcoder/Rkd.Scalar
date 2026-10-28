using System.Runtime.CompilerServices;
using Rkd.Scalar;

// Types moved in 2.10.0 to the packages other layers reference without the whole Rkd.Scalar
// (same names and namespace). Applications and libraries compiled against Rkd.Scalar 2.9 and earlier keep working.

// Rkd.Scalar.Abstractions
[assembly: TypeForwardedTo(typeof(ApiKeyCredentials))]
[assembly: TypeForwardedTo(typeof(BasicAuthCredentials))]
[assembly: TypeForwardedTo(typeof(CredentialValidationResult))]
[assembly: TypeForwardedTo(typeof(ICredentialValidator<>))]
[assembly: TypeForwardedTo(typeof(RkdScalarAuthenticationSchemes))]
[assembly: TypeForwardedTo(typeof(IJwtSigner))]
[assembly: TypeForwardedTo(typeof(IJwtSigningKeyResolver))]
[assembly: TypeForwardedTo(typeof(IJwtTokenService))]
[assembly: TypeForwardedTo(typeof(JwtLoginResponse))]
[assembly: TypeForwardedTo(typeof(JwtSigningKey))]
[assembly: TypeForwardedTo(typeof(JwtSigningKeyContext))]
[assembly: TypeForwardedTo(typeof(JwtToken))]
[assembly: TypeForwardedTo(typeof(RkdPem))]
[assembly: TypeForwardedTo(typeof(HttpLogEntry))]
[assembly: TypeForwardedTo(typeof(IHttpLogSink))]

// Rkd.Scalar.Errors
[assembly: TypeForwardedTo(typeof(IProblemLogDetails))]
[assembly: TypeForwardedTo(typeof(ProblemException))]
[assembly: TypeForwardedTo(typeof(RkdError))]
