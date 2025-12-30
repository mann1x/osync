using ByteSizeLib;
using System.Net;

namespace osync
{
    /// <summary>
    /// Stream wrapper that displays download/upload progress to console
    /// </summary>
    public class ProgressStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly long _totalBytes;
        private readonly ByteSize _totalSize;
        private long _bytesTransferred;

        public ProgressStream(Stream baseStream, long totalBytes, ByteSize totalSize)
        {
            _baseStream = baseStream;
            _totalBytes = totalBytes;
            _totalSize = totalSize;
            _bytesTransferred = 0;
        }

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _totalBytes;
        public override long Position
        {
            get => _bytesTransferred;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = _baseStream.Read(buffer, offset, count);
            _bytesTransferred += bytesRead;

            // Show progress
            int percentage = _totalBytes > 0 ? (int)((_bytesTransferred * 100) / _totalBytes) : 0;
            var transferred = ByteSize.FromBytes(_bytesTransferred);
            Console.Write($"\r  Progress: {percentage}% ({transferred.ToString("#.##")} / {_totalSize.ToString("#.##")})");

            return bytesRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int bytesRead = await _baseStream.ReadAsync(buffer, offset, count, cancellationToken);
            _bytesTransferred += bytesRead;

            // Show progress
            int percentage = _totalBytes > 0 ? (int)((_bytesTransferred * 100) / _totalBytes) : 0;
            var transferred = ByteSize.FromBytes(_bytesTransferred);
            Console.Write($"\r  Progress: {percentage}% ({transferred.ToString("#.##")} / {_totalSize.ToString("#.##")})");

            return bytesRead;
        }

        public override void Flush() => _baseStream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _baseStream?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Bounded buffer stream for simultaneous download/upload with backpressure control
    /// </summary>
    public class BufferedPipeStream : Stream
    {
        private readonly SemaphoreSlim _writeSemaphore;
        private readonly SemaphoreSlim _readSemaphore;
        private readonly Queue<byte[]> _bufferQueue;
        private readonly object _lock = new object();
        private readonly long _maxBufferSize;
        private long _currentBufferSize;
        private bool _writeCompleted;
        private Exception? _exception;

        public BufferedPipeStream(long maxBufferSize)
        {
            _maxBufferSize = maxBufferSize;
            _bufferQueue = new Queue<byte[]>();
            _writeSemaphore = new SemaphoreSlim(1, 1);
            _readSemaphore = new SemaphoreSlim(0);
            _currentBufferSize = 0;
            _writeCompleted = false;
        }

        public void CompleteWriting()
        {
            lock (_lock)
            {
                _writeCompleted = true;
                _readSemaphore.Release();
            }
        }

        public void SetException(Exception ex)
        {
            lock (_lock)
            {
                _exception = ex;
                _readSemaphore.Release();
            }
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_exception != null)
                throw _exception;

            // Create a copy of the data to write
            byte[] data = new byte[count];
            Array.Copy(buffer, offset, data, 0, count);

            // Wait if buffer is full (backpressure)
            while (true)
            {
                lock (_lock)
                {
                    if (_currentBufferSize + count <= _maxBufferSize)
                    {
                        _bufferQueue.Enqueue(data);
                        _currentBufferSize += count;
                        _readSemaphore.Release();
                        return;
                    }
                }

                // Buffer is full, wait a bit before retrying (backpressure)
                await Task.Delay(10, cancellationToken);
            }
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            while (true)
            {
                if (_exception != null)
                    throw _exception;

                lock (_lock)
                {
                    if (_bufferQueue.Count > 0)
                    {
                        byte[] data = _bufferQueue.Dequeue();
                        int bytesToCopy = Math.Min(data.Length, count);
                        Array.Copy(data, 0, buffer, offset, bytesToCopy);
                        _currentBufferSize -= data.Length;

                        // If we didn't copy all data, put the rest back
                        if (bytesToCopy < data.Length)
                        {
                            byte[] remaining = new byte[data.Length - bytesToCopy];
                            Array.Copy(data, bytesToCopy, remaining, 0, remaining.Length);
                            var tempQueue = new Queue<byte[]>();
                            tempQueue.Enqueue(remaining);
                            while (_bufferQueue.Count > 0)
                                tempQueue.Enqueue(_bufferQueue.Dequeue());
                            _bufferQueue.Clear();
                            while (tempQueue.Count > 0)
                                _bufferQueue.Enqueue(tempQueue.Dequeue());
                            _currentBufferSize += remaining.Length;
                        }

                        return bytesToCopy;
                    }
                    else if (_writeCompleted)
                    {
                        return 0; // End of stream
                    }
                }

                // Wait for data to be available
                await _readSemaphore.WaitAsync(cancellationToken);
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Stream wrapper for tracking upload progress with callback
    /// </summary>
    public class ProgressTrackingStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly long _totalBytes;
        private long _bytesRead;
        private readonly Action<long>? _progressCallback;

        public ProgressTrackingStream(Stream baseStream, long totalBytes, Action<long> progressCallback)
        {
            _baseStream = baseStream;
            _totalBytes = totalBytes;
            _progressCallback = progressCallback;
            _bytesRead = 0;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int bytesRead = await _baseStream.ReadAsync(buffer, offset, count, cancellationToken);
            _bytesRead += bytesRead;
            _progressCallback?.Invoke(_bytesRead);
            return bytesRead;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = _baseStream.Read(buffer, offset, count);
            _bytesRead += bytesRead;
            _progressCallback?.Invoke(_bytesRead);
            return bytesRead;
        }

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _totalBytes;
        public override long Position { get => _bytesRead; set => throw new NotSupportedException(); }
        public override void Flush() => _baseStream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// HttpContent wrapper that tracks upload progress
    /// From https://stackoverflow.com/a/41392145/4213397
    /// </summary>
    internal class ProgressableStreamContent : HttpContent
    {
        private const int defaultBufferSize = 5 * 4096; // 20kb buffer
        private readonly HttpContent content;
        private readonly int bufferSize;
        private readonly Action<long, long>? progress;

        public ProgressableStreamContent(HttpContent content, Action<long, long> progress)
            : this(content, defaultBufferSize, progress)
        {
        }

        public ProgressableStreamContent(HttpContent content, int bufferSize, Action<long, long> progress)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));
            if (bufferSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(bufferSize));

            this.content = content;
            this.bufferSize = bufferSize;
            this.progress = progress;

            foreach (var h in content.Headers)
            {
                this.Headers.Add(h.Key, h.Value);
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return Task.Run(async () =>
            {
                var buffer = new byte[bufferSize];
                long size;
                TryComputeLength(out size);
                long uploaded = 0;

                using (var sinput = await content.ReadAsStreamAsync())
                {
                    while (true)
                    {
                        var length = sinput.Read(buffer, 0, buffer.Length);
                        if (length <= 0) break;

                        uploaded += length;
                        progress?.Invoke(uploaded, size);

                        stream.Write(buffer, 0, length);
                        stream.Flush();
                    }
                }
                stream.Flush();
            });
        }

        protected override bool TryComputeLength(out long length)
        {
            length = content.Headers.ContentLength.GetValueOrDefault();
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                content.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
