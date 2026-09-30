using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rkd.Scalar.HttpLogging
{
    /// <summary>
    /// Reads the queue and calls the sinks with batches of the entries available at the moment (no waiting to fill
    /// a batch), isolated from each other: a failing sink does not stop the others. Queued entries are written
    /// when the application stops, within <see cref="RkdHttpLoggingOptions.ShutdownTimeout"/>.
    /// </summary>
    internal sealed partial class HttpLogWriterService : BackgroundService
    {
        private static readonly TimeSpan WarningInterval = TimeSpan.FromMinutes(1);

        private readonly HttpLogQueue _queue;

        private readonly IHttpLogSink[] _sinks;

        private readonly RkdHttpLoggingOptions _options;

        private readonly ILogger<HttpLogWriterService> _logger;

        private readonly Dictionary<IHttpLogSink, SinkState> _states = new(ReferenceEqualityComparer.Instance);

        private DateTimeOffset _lastDropWarning = DateTimeOffset.MinValue;

        public HttpLogWriterService(
            HttpLogQueue queue,
            IEnumerable<IHttpLogSink> sinks,
            RkdHttpLoggingOptions options,
            ILogger<HttpLogWriterService> logger)
        {
            _queue = queue;
            _sinks = sinks.ToArray();
            _options = options;
            _logger = logger;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _options.Validate();

            if (_options.Enabled && _sinks.Length == 0)
                throw new InvalidOperationException(
                    "WithHttpLogging() has no destination. Add a sink: WithHttpLogSink<T>(), or a package such as " +
                    "Rkd.Scalar.HttpLogging.SqlServer (WriteHttpLogsToSqlServer).");

            foreach (var sink in _sinks)
                _states[sink] = new SinkState();

            return base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var reader = _queue.Reader;
            var batch = new List<HttpLogEntry>(_options.BatchSize);

            try
            {
                while (await reader.WaitToReadAsync(stoppingToken))
                {
                    while (batch.Count < _options.BatchSize && reader.TryRead(out var entry))
                        batch.Add(entry);

                    await WriteAsync(batch, stoppingToken);
                    batch.Clear();

                    ReportDropped();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }

            // Application stopping: write what is still queued, within the shutdown timeout.
            using var shutdown = new CancellationTokenSource(_options.ShutdownTimeout);

            while (!shutdown.IsCancellationRequested && reader.TryRead(out var pending))
            {
                batch.Add(pending);

                if (batch.Count == _options.BatchSize)
                {
                    await WriteAsync(batch, shutdown.Token);
                    batch.Clear();
                }
            }

            if (batch.Count > 0 && !shutdown.IsCancellationRequested)
                await WriteAsync(batch, shutdown.Token);

            ReportDropped();
        }

        private async Task WriteAsync(List<HttpLogEntry> batch, CancellationToken cancellationToken)
        {
            if (batch.Count == 0)
                return;

            // A copy, so sinks never see the list reused for the next batch.
            var entries = batch.ToArray();

            foreach (var sink in _sinks)
            {
                var state = _states[sink];

                try
                {
                    await sink.WriteAsync(entries, cancellationToken);

                    if (state.Failures > 0)
                    {
                        LogSinkRecovered(_logger, sink.GetType().Name, state.Failures);
                        state.Failures = 0;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    state.Failures++;

                    // Never penalizes the application: log (at most once a minute per sink) and move on.
                    var now = DateTimeOffset.UtcNow;

                    if (now - state.LastWarning >= WarningInterval)
                    {
                        state.LastWarning = now;
                        LogSinkFailed(_logger, exception, sink.GetType().Name, entries.Length, state.Failures);
                    }
                }
                catch (OperationCanceledException)
                {
                    LogShutdownDiscarded(_logger, sink.GetType().Name, entries.Length);
                }
            }
        }

        private void ReportDropped()
        {
            var now = DateTimeOffset.UtcNow;

            if (now - _lastDropWarning < WarningInterval)
                return;

            var dropped = _queue.TakeDropped();

            if (dropped > 0)
            {
                _lastDropWarning = now;
                LogDropped(_logger, dropped, _options.QueueCapacity);
            }
        }

        private sealed class SinkState
        {
            public int Failures { get; set; }

            public DateTimeOffset LastWarning { get; set; } = DateTimeOffset.MinValue;
        }

        [LoggerMessage(EventId = 30, Level = LogLevel.Warning, Message = "HTTP log sink {Sink} failed to write {Count} entries ({Failures} consecutive failures); the entries were discarded.")]
        private static partial void LogSinkFailed(ILogger logger, Exception exception, string sink, int count, int failures);

        [LoggerMessage(EventId = 31, Level = LogLevel.Information, Message = "HTTP log sink {Sink} recovered after {Failures} failures.")]
        private static partial void LogSinkRecovered(ILogger logger, string sink, int failures);

        [LoggerMessage(EventId = 32, Level = LogLevel.Warning, Message = "{Dropped} HTTP log entries were dropped because the queue was full (capacity {Capacity}); the sinks are slower than the traffic.")]
        private static partial void LogDropped(ILogger logger, long dropped, int capacity);

        [LoggerMessage(EventId = 33, Level = LogLevel.Warning, Message = "HTTP log sink {Sink} did not finish writing {Count} entries before the shutdown timeout.")]
        private static partial void LogShutdownDiscarded(ILogger logger, string sink, int count);
    }
}
