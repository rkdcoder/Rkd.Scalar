using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Microsoft.AspNetCore.Http.Features;

namespace Rkd.Scalar.HttpLogging
{
    /// <summary>
    /// Keeps the first <c>limit</c> bytes that go through a stream. Nothing is buffered beyond that.
    /// </summary>
    internal sealed class BoundedCapture
    {
        private readonly int _limit;

        private byte[]? _buffer;

        private int _count;

        public BoundedCapture(int limit)
        {
            _limit = limit;
        }

        public bool Truncated { get; private set; }

        public long Total { get; private set; }

        public void Append(ReadOnlySpan<byte> data)
        {
            Total += data.Length;

            if (data.IsEmpty || Truncated)
                return;

            var available = _limit - _count;
            var take = Math.Min(available, data.Length);

            if (take > 0)
            {
                _buffer ??= ArrayPool<byte>.Shared.Rent(Math.Min(_limit, 4096));

                if (_buffer.Length < _count + take)
                {
                    var larger = ArrayPool<byte>.Shared.Rent(Math.Min(_limit, Math.Max(_buffer.Length * 2, _count + take)));
                    _buffer.AsSpan(0, _count).CopyTo(larger);
                    ArrayPool<byte>.Shared.Return(_buffer);
                    _buffer = larger;
                }

                data[..take].CopyTo(_buffer.AsSpan(_count));
                _count += take;
            }

            if (take < data.Length)
                Truncated = true;
        }

        /// <summary>Decodes the captured bytes (UTF-8) and releases the buffer.</summary>
        public string? GetTextAndRelease()
        {
            if (_buffer is null)
                return null;

            var text = Encoding.UTF8.GetString(_buffer, 0, _count);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;

            return text;
        }
    }

    /// <summary>Request body wrapper: captures what the application reads.</summary>
    internal sealed class CapturingRequestStream : Stream
    {
        private readonly Stream _inner;

        public CapturingRequestStream(Stream inner, int limit)
        {
            _inner = inner;
            Capture = new BoundedCapture(limit);
        }

        public BoundedCapture Capture { get; }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            Capture.Append(buffer.AsSpan(offset, read));
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            Capture.Append(buffer.Span[..read]);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => _inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Response body feature that writes through to the original one and keeps the first bytes of text responses.
    /// Streaming responses (server-sent events, large downloads) keep streaming; files sent with
    /// <c>SendFileAsync</c> are not captured.
    /// </summary>
    internal sealed class CapturingResponseBody : Stream, IHttpResponseBodyFeature
    {
        private readonly IHttpResponseBodyFeature _inner;

        private readonly Func<bool> _shouldCapture;

        private bool? _capturing;

        private PipeWriter? _writer;

        public CapturingResponseBody(IHttpResponseBodyFeature inner, int limit, Func<bool> shouldCapture)
        {
            _inner = inner;
            _shouldCapture = shouldCapture;
            Capture = new BoundedCapture(limit);
        }

        public BoundedCapture Capture { get; }

        public bool Captured => _capturing == true;

        public Stream Stream => this;

        public PipeWriter Writer => _writer ??= PipeWriter.Create(this, new StreamPipeWriterOptions(leaveOpen: true));

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void DisableBuffering() => _inner.DisableBuffering();

        public Task StartAsync(CancellationToken cancellationToken = default) => _inner.StartAsync(cancellationToken);

        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default) =>
            _inner.SendFileAsync(path, offset, count, cancellationToken);

        public async Task CompleteAsync()
        {
            await FlushWriterAsync();
            await _inner.CompleteAsync();
        }

        /// <summary>Flushes what was written through <see cref="Writer"/> before the original feature is restored.</summary>
        public async ValueTask FlushWriterAsync()
        {
            if (_writer is not null)
            {
                await _writer.CompleteAsync();
                _writer = null;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Observe(buffer.AsSpan(offset, count));
            _inner.Stream.Write(buffer, offset, count);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Observe(buffer.Span);
            return _inner.Stream.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => _inner.Stream.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.Stream.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        private long _uncapturedBytes;

        private void Observe(ReadOnlySpan<byte> data)
        {
            // The content type is known once the application starts writing.
            _capturing ??= _shouldCapture();

            if (_capturing == true)
                Capture.Append(data);
            else
                _uncapturedBytes += data.Length;
        }

        /// <summary>Bytes written to the response body.</summary>
        public long Size => Capture.Total + _uncapturedBytes;
    }
}
