namespace Rkd.Scalar
{
    /// <summary>
    /// Credentials of an HTTP Basic <c>Authorization</c> header.
    /// </summary>
    /// <param name="Username">The user name.</param>
    /// <param name="Password">The password.</param>
    public sealed record BasicAuthCredentials(string Username, string Password);
}
