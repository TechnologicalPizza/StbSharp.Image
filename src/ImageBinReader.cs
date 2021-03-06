using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace StbSharp.ImageRead
{
    public class ImageBinReader : IDisposable
    {
        private int _bufferOffset;
        private int _bufferLength;
        private long _position;

        public Stream Stream { get; private set; }
        public byte[] Buffer { get; private set; }
        public CancellationToken CancellationToken { get; set; }

        public bool IsDisposed => Buffer == null;
        public long Position => _position + _bufferOffset;

        public ImageBinReader(Stream stream, byte[] buffer)
        {
            Stream = stream ?? throw new ArgumentNullException(nameof(stream));
            Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        }

        private Span<byte> Take(int count)
        {
            if (IsDisposed)
                throw new ObjectDisposedException(GetType().FullName);

            if (_bufferLength < count)
                throw new EndOfStreamException();

            var slice = Buffer.AsSpan(_bufferOffset, count);
            _bufferOffset += count;
            _bufferLength -= count;
            return slice;
        }

        private void FillBuffer()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(GetType().FullName);

            Buffer.AsSpan(_bufferOffset, _bufferLength).CopyTo(Buffer);

            _position += _bufferOffset;
            _bufferOffset = 0;

            while (_bufferLength < Buffer.Length)
            {
                CancellationToken.ThrowIfCancellationRequested();

                var slice = Buffer.AsSpan(_bufferLength);
                int read = Stream.Read(slice);
                if (read == 0)
                    break;

                _bufferLength += read;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FillBufferAndCheck(int count)
        {
            FillBuffer();

            if (_bufferLength < count)
                throw new EndOfStreamException();
        }

        /// <summary>
        /// </summary>
        /// <exception cref="EndOfStreamException"/>
        public void Skip(long count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (count == 0)
                return;

            if (_bufferLength > 0)
            {
                int toRead = (int)Math.Min(count, _bufferLength);
                _bufferOffset += toRead;
                _bufferLength -= toRead;
                count -= toRead;

                if (count == 0)
                    return;
            }

            CancellationToken.ThrowIfCancellationRequested();

            do
            {
                int toRead = (int)Math.Min(count, Buffer.Length);
                Span<byte> slice = Buffer.AsSpan(0, toRead);
                int read = Stream.Read(slice);
                if (read == 0)
                    break;

                count -= read;
                _position += read;
            }
            while (count > 0);

            if (count != 0)
                throw new EndOfStreamException();
        }

        public bool TryReadBytes(Span<byte> destination)
        {
            if (destination.IsEmpty)
                return true;

            // TODO: read into buffer if destination is small

            if (_bufferLength > 0)
            {
                int toRead = Math.Min(destination.Length, _bufferLength);
                Take(toRead).CopyTo(destination);
                destination = destination[toRead..];
            }

            while (!destination.IsEmpty)
            {
                CancellationToken.ThrowIfCancellationRequested();

                int read = Stream.Read(destination);
                if (read == 0)
                    break;

                destination = destination[read..];
                _position += read;
            }

            return destination.IsEmpty;
        }

        /// <summary>
        /// </summary>
        /// <exception cref="EndOfStreamException"/>
        public void ReadBytes(Span<byte> destination)
        {
            if (!TryReadBytes(destination))
                throw new EndOfStreamException();
        }

        public int TryReadByte()
        {
            if (_bufferLength < sizeof(byte))
            {
                FillBuffer();

                if (_bufferLength < sizeof(byte))
                    return -1;
            }

            byte value = Buffer[_bufferOffset];
            _bufferOffset += sizeof(byte);
            _bufferLength -= sizeof(byte);
            return value;
        }

        /// <summary>
        /// </summary>
        /// <exception cref="EndOfStreamException"/>
        public byte ReadByte()
        {
            if (_bufferLength < sizeof(byte))
                FillBufferAndCheck(sizeof(byte));

            byte value = Buffer[_bufferOffset];
            _bufferOffset += sizeof(byte);
            _bufferLength -= sizeof(byte);
            return value;
        }

        /// <summary>
        /// </summary>
        /// <exception cref="EndOfStreamException"/>
        public short ReadInt16LE()
        {
            if (_bufferLength < sizeof(short))
                FillBufferAndCheck(sizeof(short));
            return BinaryPrimitives.ReadInt16LittleEndian(Take(sizeof(short)));
        }

        /// <summary>
        /// </summary>
        /// <exception cref="EndOfStreamException"/>
        public short ReadInt16BE()
        {
            if (_bufferLength < sizeof(short))
                FillBufferAndCheck(sizeof(short));
            return BinaryPrimitives.ReadInt16BigEndian(Take(sizeof(short)));
        }

        /// <summary>
        /// </summary>
        /// <exception cref="EndOfStreamException"/>
        [CLSCompliant(false)]
        public ushort ReadUInt16LE()
        {
            if (_bufferLength < sizeof(ushort))
                FillBufferAndCheck(sizeof(ushort));
            return BinaryPrimitives.ReadUInt16LittleEndian(Take(sizeof(ushort)));
        }

        /// <summary>
        /// </summary>
        /// <exception cref="EndOfStreamException"/>
        [CLSCompliant(false)]
        public ushort ReadUInt16BE()
        {
            if (_bufferLength < sizeof(ushort))
                FillBufferAndCheck(sizeof(ushort));
            return BinaryPrimitives.ReadUInt16BigEndian(Take(sizeof(ushort)));
        }

        /// <summary>
        /// </summary>
        /// <exception cref="EndOfStreamException"/>
        public int ReadInt32LE()
        {
            if (_bufferLength < sizeof(int))
                FillBufferAndCheck(sizeof(int));
            return BinaryPrimitives.ReadInt32LittleEndian(Take(sizeof(int)));
        }

        /// <summary>
        /// </summary>
        /// <exception cref="EndOfStreamException"/>
        [CLSCompliant(false)]
        public uint ReadUInt32LE()
        {
            if (_bufferLength < sizeof(uint))
                FillBufferAndCheck(sizeof(uint));
            return BinaryPrimitives.ReadUInt32LittleEndian(Take(sizeof(uint)));
        }

        /// <summary>
        /// </summary>
        /// <exception cref="EndOfStreamException"/>
        public int ReadInt32BE()
        {
            if (_bufferLength < sizeof(int))
                FillBufferAndCheck(sizeof(int));
            return BinaryPrimitives.ReadInt32BigEndian(Take(sizeof(int)));
        }

        /// <summary>
        /// </summary>
        /// <exception cref="EndOfStreamException"/>
        [CLSCompliant(false)]
        public uint ReadUInt32BE()
        {
            if (_bufferLength < sizeof(uint))
                FillBufferAndCheck(sizeof(uint));
            return BinaryPrimitives.ReadUInt32BigEndian(Take(sizeof(uint)));
        }

        #region IDisposable

        protected virtual void Dispose(bool disposing)
        {
            Stream = null!;
            Buffer = null!;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}