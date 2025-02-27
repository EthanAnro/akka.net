﻿//-----------------------------------------------------------------------
// <copyright file="DisabledBufferPool.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.IO.Buffers
{
    using ByteBuffer = ArraySegment<byte>;
    
    internal class DisabledBufferPool : IBufferPool
    {
        private readonly int _bufferSize;

        public DisabledBufferPool(ExtendedActorSystem system, Config config) : this(config.GetInt("buffer-size", 256))
        {
        }

        public DisabledBufferPool(int bufferSize)
        {
            if (bufferSize <= 0) throw new ArgumentException("Buffer size must be positive number", nameof(bufferSize));

            _bufferSize = bufferSize;
        }

        public ByteBuffer Rent() => RentOfSize(_bufferSize);

        public IEnumerable<ByteBuffer> Rent(int minimumSize)
        {
            var bytesRequired = Math.Max(minimumSize, _bufferSize);
            return new [] { RentOfSize(bytesRequired) }; 
        }

        public void Release(ByteBuffer buf)
        {
            // Let GC to collect this
        }

        public void Release(IEnumerable<ByteBuffer> buffers)
        {
            foreach (var buf in buffers)
            {
                Release(buf);
            }
        }

        public BufferPoolInfo Diagnostics() => new(typeof(DisabledBufferPool), 0, 0, 0);

        private ByteBuffer RentOfSize(int size)
        {
            var bytes = new byte[size];
            return new ByteBuffer(bytes, 0, size);
        }
    }
}
