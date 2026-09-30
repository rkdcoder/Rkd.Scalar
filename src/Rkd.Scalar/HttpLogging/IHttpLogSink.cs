namespace Rkd.Scalar
{
    /// <summary>
    /// Destination of the HTTP logs captured by <c>WithHttpLogging()</c>: a database, a file, a queue, an HTTP
    /// endpoint… Rkd.Scalar captures each request, queues it in memory and calls the sinks in batches from a
    /// background service, so a slow or failing sink never delays or breaks a request.
    /// </summary>
    /// <remarks>
    /// Sinks are singletons (use <c>IServiceScopeFactory</c> for scoped dependencies such as a <c>DbContext</c>).
    /// Exceptions are logged and the batch is discarded; entries must not be kept after the call returns.
    /// Ready-made sinks: <c>Rkd.Scalar.HttpLogging.SqlServer</c>.
    /// </remarks>
    /// <example>
    /// <code>
    /// public sealed class ConsoleHttpLogSink : IHttpLogSink
    /// {
    ///     public Task WriteAsync(IReadOnlyList&lt;HttpLogEntry&gt; entries, CancellationToken cancellationToken)
    ///     {
    ///         foreach (var entry in entries)
    ///             Console.WriteLine($"{entry.Method} {entry.Path} {entry.StatusCode} {entry.Duration.TotalMilliseconds} ms");
    ///         return Task.CompletedTask;
    ///     }
    /// }
    ///
    /// builder.AddRkdScalar().WithHttpLogSink&lt;ConsoleHttpLogSink&gt;();
    /// </code>
    /// </example>
    public interface IHttpLogSink
    {
        /// <summary>Persists a batch of entries (1 to <see cref="RkdHttpLoggingOptions.BatchSize"/> items).</summary>
        /// <param name="entries">Entries, in the order the requests finished.</param>
        /// <param name="cancellationToken">Cancelled when the application stops and the shutdown timeout is over.</param>
        Task WriteAsync(IReadOnlyList<HttpLogEntry> entries, CancellationToken cancellationToken);
    }
}
