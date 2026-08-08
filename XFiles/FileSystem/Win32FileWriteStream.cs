using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;

namespace XFiles.FileSystem
{
    /// <summary>
    /// System.IO.Stream backed by Win32 P/Invoke (CreateFileFromAppW + WriteFile).
    /// Required because System.IO.FileStream doesn't work in UWP sandbox on Xbox.
    /// Supports Write + Seek (needed for SharpCompress archive output).
    /// </summary>
    internal class Win32FileWriteStream : Stream
    {
        private readonly IntPtr _handle;
        private long _position;
        private bool _disposed;

        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint CREATE_ALWAYS = 2;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileFromAppW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(
            IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFilePointerEx(
            IntPtr hFile, long lDistanceToMove, out long lpNewFilePointer, uint dwMoveMethod);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint FILE_BEGIN = 0;
        private const uint FILE_CURRENT = 1;
        private const uint FILE_END = 2;

        public static Win32FileWriteStream Create(string filePath)
        {
            IntPtr hFile = CreateFileFromAppW(
                filePath, GENERIC_WRITE,
                FILE_SHARE_READ,
                IntPtr.Zero, CREATE_ALWAYS,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (hFile == (IntPtr)(-1))
            {
                Log.Warn("Win32FileWriteStream.Create: cannot open {Path} for write, Win32 error={Error}", filePath, Marshal.GetLastWin32Error());
                return null;
            }

            return new Win32FileWriteStream(hFile);
        }

        private Win32FileWriteStream(IntPtr handle)
        {
            _handle = handle;
            _position = 0;
        }

        public override bool CanWrite => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanRead => false;
        public override long Length => throw new NotSupportedException("Length not supported for write-only stream");

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Win32FileWriteStream));

            byte[] writeBuf = buffer;
            byte[] poolBuf = null;

            if (offset != 0)
            {
                poolBuf = ArrayPool<byte>.Shared.Rent(count);
                Array.Copy(buffer, offset, poolBuf, 0, count);
                writeBuf = poolBuf;
            }

            try
            {
                int remaining = count;
                while (remaining > 0)
                {
                    uint bytesWritten;
                    bool ok = WriteFile(_handle, writeBuf, (uint)remaining, out bytesWritten, IntPtr.Zero);

                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        Log.Err("Win32FileWriteStream.Write: WriteFile failed, {Count} bytes pending, Win32 error={Error}", null, remaining, err);
                        throw new IOException($"WriteFile failed, Win32 error={err}");
                    }

                    if (bytesWritten == 0)
                        throw new IOException("WriteFile wrote 0 bytes");

                    remaining -= (int)bytesWritten;

                    if (remaining > 0)
                        Array.Copy(writeBuf, (int)bytesWritten, writeBuf, 0, remaining);
                }

                _position += count;
            }
            finally
            {
                if (poolBuf != null)
                    ArrayPool<byte>.Shared.Return(poolBuf);
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Win32FileWriteStream));

            uint method;
            switch (origin)
            {
                case SeekOrigin.Begin: method = FILE_BEGIN; break;
                case SeekOrigin.Current: method = FILE_CURRENT; break;
                case SeekOrigin.End: method = FILE_END; break;
                default: throw new ArgumentException("Invalid SeekOrigin");
            }

            long newPos;
            if (!SetFilePointerEx(_handle, offset, out newPos, method))
                throw new IOException("SetFilePointerEx failed");

            _position = newPos;
            return _position;
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                CloseHandle(_handle);
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
