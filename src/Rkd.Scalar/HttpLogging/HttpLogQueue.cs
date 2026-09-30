using System.Threading.Channels;

namespace Rkd.Scalar.HttpLogging
{
    /// <summary>
    /// Bounded in-memory queue between the requests and the sinks. Writing never waits: when full, the entry is dropped.
    /// </summary>
    internal sealed class HttpLogQueue
    {
        private readonly Channel<HttpLogEntry> _channel;

        private long _dropped;

        public HttpLogQueue(RkdHttpLoggingOptions options)
        {
            _channel = Channel.CreateBounded<HttpLogEntry>(new BoundedChannelOptions(options.QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });
        }

        public ChannelReader<HttpLogEntry> Reader => _channel.Reader;

        public void Enqueue(HttpLogEntry entry)
        {
            if (!_channel.Writer.TryWrite(entry))
                Interlocked.Increment(ref _dropped);
        }

        /// <summary>Entries dropped since the last call.</summary>
        public long TakeDropped() => Interlocked.Exchange(ref _dropped, 0);
    }
}
