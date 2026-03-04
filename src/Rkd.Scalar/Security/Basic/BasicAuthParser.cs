using System.Net.Http.Headers;
using System.Text;

namespace Rkd.Scalar.Security.Basic
{
    public static class BasicAuthParser
    {
        public static bool TryParse(
            string header,
            out BasicAuthCredentials credentials)
        {
            credentials = default!;

            try
            {
                var authHeader = AuthenticationHeaderValue.Parse(header);

                if (!authHeader.Scheme.Equals(
                    "Basic",
                    StringComparison.OrdinalIgnoreCase))
                    return false;

                var decoded = Encoding.UTF8.GetString(
                    Convert.FromBase64String(authHeader.Parameter ?? "")
                );

                var parts = decoded.Split(':', 2);

                if (parts.Length != 2)
                    return false;

                credentials = new BasicAuthCredentials(
                    parts[0],
                    parts[1]);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
