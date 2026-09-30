using Microsoft.AspNetCore.Http;

namespace Rkd.Scalar.Errors
{
    /// <summary>
    /// Keeps the <see cref="ProblemException"/> an authentication handler failed with (e.g.
    /// <c>context.Fail(RkdError.Unauthorized("WRONG_ENVIRONMENT", "…"))</c> in <c>OnTokenValidated</c>), so the 401
    /// problem details carry its <c>code</c> and <c>detail</c>. Other failures stay generic: their messages are
    /// technical and must not reach the client.
    /// </summary>
    internal static class AuthenticationFailures
    {
        private static readonly object Key = new();

        public static void Capture(HttpContext context, Exception? failure)
        {
            if (Find(failure) is { } problem)
                context.Items[Key] = problem;
        }

        public static ProblemException? Get(HttpContext context) =>
            context.Items.TryGetValue(Key, out var value) ? value as ProblemException : null;

        private static ProblemException? Find(Exception? exception)
        {
            for (var depth = 0; exception is not null && depth < 8; depth++)
            {
                if (exception is ProblemException problem)
                    return problem;

                if (exception is AggregateException aggregate)
                    return aggregate.InnerExceptions.Select(Find).FirstOrDefault(p => p is not null);

                exception = exception.InnerException;
            }

            return null;
        }
    }
}
