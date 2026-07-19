using System;
using System.IO;
using System.Runtime.InteropServices;

namespace XFiles.FileSystem
{
    /// <summary>
    /// System.IO.Stream backed by Win32 P/Invoke (CreateFileFromAppW + ReadFile).
    /// Required because System.IO.FileStream doesn't work in UWP sandbox on Xbox.
    /// Supports Read + Seek (needed by SharpCompress). Write not implemented.
    /// </summary>
    internal class Win32FileStream : Stream
    {
        private readonly IntPtr _handle;
        private long _position;
        private readonly long _length;
        private bool _disposed;

        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileFromAppW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(
            IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileSizeEx(IntPtr hFile, out long lpFileSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFilePointerEx(
            IntPtr hFile, long lDistanceToMove, out long lpNewFilePointer, uint dwMoveMethod);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint FILE_BEGIN = 0;
        private const uint FILE_CURRENT = 1;
        private const uint FILE_END = 2;

        public static Win32FileStream OpenRead(string filePath)
        {
            IntPtr hFile = CreateFileFromAppW(
                filePath, GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN,
                IntPtr.Zero);

            if (hFile == (IntPtr)(-1))
                return null;

            long length;
            if (!GetFileSizeEx(hFile, out length))
            {
                CloseHandle(hFile);
                return null;
            }

            return new Win32FileStream(hFile, length);
        }

        private Win32FileStream(IntPtr handle, long length)
        {
            _handle = handle;
            _length = length;
            _position = 0;
        }

        public override bool CanRead => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Win32FileStream));

            byte[] readBuf = buffer;
            int readOffset = offset;

            // If offset != 0, Win32 ReadFile needs a contiguous buffer
            if (offset != 0)
            {
                readBuf = new byte[count];
                readOffset = 0;
            }

            uint bytesRead;
            bool ok = ReadFile(_handle, readBuf, (uint)count, out bytesRead, IntPtr.Zero);

            if (!ok || bytesRead == 0)
                return 0;

            if (offset != 0)
                Array.Copy(readBuf, 0, buffer, offset, (int)bytesRead);

            _position += bytesRead;
            return (int)bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Win32FileStream));

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
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

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
