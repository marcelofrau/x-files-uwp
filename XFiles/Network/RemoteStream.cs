using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace XFiles.Network
{
    /// <summary>
    /// A blocking <see cref="IRandomAccessStream"/> over a remote (SMB) file stream.
    /// Every read pulls exactly what the consumer asks for from the server — no full
    /// download, no pre-buffer. This is what lets audio/video start playing in
    /// ~1-2s instead of after the whole file transfers. The underlying stream is
    /// disposed with this stream (the SMB session itself stays pooled).
    /// </summary>
    public sealed class RemoteStream : IRandomAccessStream, IDisposable
    {
        private readonly Stream _underlying;
        private readonly object _gate = new object();
        private readonly Func<Stream> _reopener;
        private bool _disposed;

        public RemoteStream(Stream underlying)
            : this(underlying, null)
        {
        }

        /// <summary>
        /// Creates a stream that can also be cloned. The media pipeline (video
        /// especially) clones the source stream to probe properties (duration,
        /// thumbnails) while playback uses the original; without a reopen factory
        /// the clone would share state and corrupt the read position.
        /// <paramref name="reopener"/> must return a fresh, independent stream over
        /// the same remote file at position 0. It runs on a background media thread,
        /// never the UI thread.
        /// </summary>
        public RemoteStream(Stream underlying, Func<Stream> reopener)
        {
            _underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
            _reopener = reopener;
        }

        public bool CanRead => true;

        public bool CanWrite => false;

        public ulong Size
        {
            get
            {
                ThrowIfDisposed();
                return (ulong)_underlying.Length;
            }
            set
            {
                // Read-only stream.
                throw new NotSupportedException("RemoteStream is read-only.");
            }
        }

        public ulong Position
        {
            get
            {
                ThrowIfDisposed();
                return (ulong)_underlying.Position;
            }
        }

        public void Seek(ulong position)
        {
            ThrowIfDisposed();
            lock (_gate)
            {
                _underlying.Seek((long)position, SeekOrigin.Begin);
            }
        }

        public IInputStream GetInputStreamAt(ulong position)
        {
            Seek(position);
            return this;
        }

        public IOutputStream GetOutputStreamAt(ulong position)
        {
            throw new NotSupportedException("RemoteStream is read-only.");
        }

        public IRandomAccessStream CloneStream()
        {
            ThrowIfDisposed();
            if (_reopener == null)
                throw new NotSupportedException("RemoteStream cannot be cloned without a reopen factory.");
            Stream clone = _reopener();
            if (clone == null)
                throw new NotSupportedException("RemoteStream clone failed to reopen the remote file.");
            return new RemoteStream(clone, _reopener);
        }

        public IAsyncOperationWithProgress<IBuffer, uint> ReadAsync(
            IBuffer buffer, uint count, InputStreamOptions options)
        {
            ThrowIfDisposed();
            return new ReadOperation(this, count);
        }

        private uint _readProgress;

        /// <summary>
        /// Minimal IAsyncOperationWithProgress implementation for ReadAsync —
        /// the WindowsRuntimeSystemExtensions AsAsync* helpers are not available
        /// in this UWP/.NET Native target.
        /// </summary>
        private sealed class ReadOperation : IAsyncOperationWithProgress<IBuffer, uint>
        {
            private readonly RemoteStream _owner;
            private readonly uint _count;
            private readonly TaskCompletionSource<IBuffer> _tcs =
                new TaskCompletionSource<IBuffer>(TaskCreationOptions.RunContinuationsAsynchronously);

            public ReadOperation(RemoteStream owner, uint count)
            {
                _owner = owner;
                _count = count;
                Task.Run(RunAsync);
            }

            public uint Id { get; private set; }

            public AsyncStatus Status => _tcs.Task.IsCompleted
                ? (_tcs.Task.IsFaulted ? AsyncStatus.Error : AsyncStatus.Completed)
                : AsyncStatus.Started;

            public Exception ErrorCode => _tcs.Task.IsFaulted
                ? _tcs.Task.Exception.InnerException
                : null;

            public void Cancel() { }

            public void Close() { }

            public IBuffer GetResults() => _tcs.Task.GetAwaiter().GetResult();

            public AsyncOperationWithProgressCompletedHandler<IBuffer, uint> Completed { get; set; }

            public AsyncOperationProgressHandler<IBuffer, uint> Progress { get; set; }

            private async Task RunAsync()
            {
                try
                {
                    byte[] tmp = new byte[_count];
                    int total = 0;
                    lock (_owner._gate)
                    {
                        while (total < _count)
                        {
                            int read = _owner._underlying.Read(tmp, total, (int)_count - total);
                            if (read <= 0) break;
                            total += read;
                            _owner._readProgress = (uint)total;
                        }
                    }
                    IBuffer result;
                    if (total == 0)
                    {
                        result = new byte[0].AsBuffer();
                    }
                    else
                    {
                        byte[] data = new byte[total];
                        Array.Copy(tmp, data, total);
                        result = data.AsBuffer();
                    }
                    _tcs.TrySetResult(result);
                    Completed?.Invoke(this, Status);
                }
                catch (Exception ex)
                {
                    _tcs.TrySetException(ex);
                    Completed?.Invoke(this, Status);
                }
                await System.Threading.Tasks.Task.CompletedTask;
            }
        }

        public IAsyncOperation<bool> FlushAsync()
        {
            throw new NotSupportedException("RemoteStream is read-only.");
        }

        public IAsyncOperationWithProgress<uint, uint> WriteAsync(IBuffer buffer)
        {
            throw new NotSupportedException("RemoteStream is read-only.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _underlying.Dispose();
            }
            catch
            {
                // The SMB handle release is best-effort; the pooled session
                // survives regardless.
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RemoteStream));
        }
    }
}
