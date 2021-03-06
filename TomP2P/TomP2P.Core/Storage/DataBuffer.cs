﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TomP2P.Extensions;
using TomP2P.Extensions.Netty.Buffer;

namespace TomP2P.Core.Storage
{
    public class DataBuffer : IEquatable<DataBuffer>
    {
        private readonly IList<ByteBuf> _buffers;

        public int AlreadyTransferred { private set; get; }

        public DataBuffer()
            : this(1)
        { }

        public DataBuffer(int nrOfBuffers)
        {
            _buffers = new List<ByteBuf>(nrOfBuffers);
        }

        public DataBuffer(sbyte[] buffer, int offset, int length)
        {
            _buffers = new List<ByteBuf>(1);
            var buf = Unpooled.WrappedBuffer(buffer, offset, length);
            _buffers.Add(buf);
        }

        /// <summary>
        /// Creates a DataBuffer and adds the MemoryStream to it.
        /// </summary>
        /// <param name="buf"></param>
        public DataBuffer(ByteBuf buf)
        {
            _buffers = new List<ByteBuf>(1);
            _buffers.Add(buf.Slice());
        }

        public DataBuffer(IList<ByteBuf> buffers)
        {
            _buffers = new List<ByteBuf>(buffers.Count);
            foreach (var buf in buffers)
            {
                _buffers.Add(buf.Duplicate());
                // TODO retain needed?
            }
        }

        public DataBuffer Add(DataBuffer dataBuffer)
        {
            lock (_buffers)
            {
                foreach (var buf in dataBuffer._buffers)
                {
                    _buffers.Add(buf.Duplicate());
                    // TODO retain needed?
                }
            }
            return this;
        }

        /// <summary>
        /// From here, work with shallow copies.
        /// </summary>
        /// <returns>Shallow copy of this DataBuffer.</returns>
        public DataBuffer ShallowCopy()
        {
            DataBuffer db;
            lock (_buffers)
            {
                db = new DataBuffer(_buffers);
            }
            return db;
        }

        /// <summary>
        /// Gets the backing list of MemoryStreams.
        /// </summary>
        /// <returns>The backing list of MemoryStreams.</returns>
        public IList<MemoryStream> BufferList()
        {
            DataBuffer copy = ShallowCopy();
            IList<MemoryStream> buffers = new List<MemoryStream>(copy._buffers.Count);
            foreach (var buf in copy._buffers)
            {
                foreach (var bb in buf.NioBuffers())
                {
                    buffers.Add(bb);
                }
            }
            return buffers;
        }

        public ByteBuf ToByteBuf()
        {
            DataBuffer copy = ShallowCopy();
            return Unpooled.WrappedBuffer(copy._buffers.ToArray());
        }

        public ByteBuf[] ToByteBufs()
        {
            DataBuffer copy = ShallowCopy();
            return copy._buffers.ToArray();
        }

        public MemoryStream[] ToByteBuffer() // TODO use possible ByteBuffer wrapper
        {
            return ToByteBuf().NioBuffers();
        }

        public void TransferTo(AlternativeCompositeByteBuf buf)
        {
            DataBuffer copy = ShallowCopy();
            foreach (var buffer in copy._buffers)
            {
                buf.AddComponent(buffer);
                AlreadyTransferred += buffer.ReadableBytes;
            }
        }

        public int TransferFrom(ByteBuf buf, int remaining)
        {
            int readable = buf.ReadableBytes;
            int index = buf.ReaderIndex;
            int length = Math.Min(remaining, readable);

            if (length == 0)
            {
                return 0;
            }

            if (buf is AlternativeCompositeByteBuf)
            {
                IList<ByteBuf> decoms =  ((AlternativeCompositeByteBuf) buf).Decompose(index, length);
                foreach (var decom in decoms)
                {
                    lock (_buffers)
                    {
                        // this is already a slice
                        _buffers.Add(decom);
                    }
                }
            }
            else
            {
                lock (_buffers)
                {
                    _buffers.Add(buf.Slice(index, length));
                }
            }

            AlreadyTransferred += length;
            buf.SetReaderIndex(buf.ReaderIndex + length);
            return length;
        }

        public void ResetAlreadyTransferred()
        {
            AlreadyTransferred = 0;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(obj, null))
            {
                return false;
            }
            if (ReferenceEquals(this, obj))
            {
                return true;
            }
            if (GetType() != obj.GetType())
            {
                return false;
            }
            return Equals(obj as DataBuffer);
        }

        public bool Equals(DataBuffer other)
        {

            return other.ToByteBuf().Equals(ToByteBuf());
        }

        public override int GetHashCode()
        {
            return ToByteBuffer().GetHashCode();
        }

        public int Length
        {
            get
            {
                int length = 0;
                DataBuffer copy = ShallowCopy();
                foreach (var buffer in copy._buffers)
                {
                    length += buffer.WriterIndex;
                }
                return length;
            }
        }

        public byte[] Bytes
        {
            get
            {
                var bufs = ToByteBuffer();
                int bufsLength = bufs.Length;
                long size = 0;
                for (int i = 0; i < bufsLength; i++)
                {
                    size += bufs[i].Remaining();
                }

                byte[] retVal = new byte[size];
                long offset = 0;
                for (int i = 0; i < bufsLength; i++)
                {
                    long remaining = bufs[i].Remaining();
                    bufs[i].Get(retVal, offset, remaining);
                    offset += remaining;
                }
                return retVal;
            }
        }
    }
}
