﻿//-----------------------------------------------------------------------
// <copyright file="IOSinks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Actors;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.IO;

namespace Akka.Streams.Implementation.IO
{
    /// <summary>
    /// INTERNAL API
    /// Creates simple synchronous Sink which writes all incoming elements to the given file
    /// (creating it before hand if necessary).
    /// </summary>
    internal sealed class FileSink : SinkModule<ByteString, Task<IOResult>>
    {
        private readonly FileInfo _f;
        private readonly long _startPosition;
        private readonly FileMode _fileMode;
        private readonly bool _autoFlush;
        private readonly FlushSignaler _flushSignaler;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="f">TBD</param>
        /// <param name="startPosition">TBD</param>
        /// <param name="fileMode">TBD</param>
        /// <param name="attributes">TBD</param>
        /// <param name="shape">TBD</param>
        /// <param name="autoFlush"></param>
        /// <param name="flushSignaler"></param>
        public FileSink(FileInfo f, long startPosition, FileMode fileMode, Attributes attributes, SinkShape<ByteString> shape, bool autoFlush, FlushSignaler flushSignaler) : base(shape)
        {
            _f = f;
            _startPosition = startPosition;
            _fileMode = fileMode;
            Attributes = attributes;
            _autoFlush = autoFlush;
            _flushSignaler = flushSignaler;

            Label = $"FileSink({f}, {fileMode})";
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected override string Label { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new FileSink(_f, _startPosition, _fileMode, attributes, AmendShape(attributes), _autoFlush, _flushSignaler);


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected override SinkModule<ByteString, Task<IOResult>> NewInstance(SinkShape<ByteString> shape)
            => new FileSink(_f, _startPosition, _fileMode, Attributes, shape, _autoFlush, _flushSignaler);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public override object Create(MaterializationContext context, out Task<IOResult> materializer)
        {
            var mat = ActorMaterializerHelper.Downcast(context.Materializer);
            var settings = mat.EffectiveSettings(context.EffectiveAttributes);

            var ioResultPromise = new TaskCompletionSource<IOResult>();
            var props = FileSubscriber.Props(_f, ioResultPromise, settings.MaxInputBufferSize, _startPosition, _fileMode, _autoFlush, _flushSignaler);

            var actorRef = mat.ActorOf(
                context, 
                props.WithDispatcher(context
                    .EffectiveAttributes
                    .GetMandatoryAttribute<ActorAttributes.Dispatcher>()
                    .Name));
            materializer = ioResultPromise.Task;
            return new ActorSubscriberImpl<ByteString>(actorRef);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// Creates simple synchronous  Sink which writes all incoming elements to the given file
    /// (creating it before hand if necessary).
    /// </summary>
    internal sealed class OutputStreamSink : SinkModule<ByteString, Task<IOResult>>
    {
        private readonly Func<Stream> _createOutput;
        private readonly bool _autoFlush;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="createOutput">TBD</param>
        /// <param name="attributes">TBD</param>
        /// <param name="shape">TBD</param>
        /// <param name="autoFlush">TBD</param>
        public OutputStreamSink(Func<Stream> createOutput, Attributes attributes, SinkShape<ByteString> shape, bool autoFlush) : base(shape)
        {
            _createOutput = createOutput;
            Attributes = attributes;
            _autoFlush = autoFlush;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new OutputStreamSink(_createOutput, attributes, AmendShape(attributes), _autoFlush);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected override SinkModule<ByteString, Task<IOResult>> NewInstance(SinkShape<ByteString> shape)
            => new OutputStreamSink(_createOutput, Attributes, shape, _autoFlush);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public override object Create(MaterializationContext context, out Task<IOResult> materializer)
        {
            var mat = ActorMaterializerHelper.Downcast(context.Materializer);
            var settings = mat.EffectiveSettings(context.EffectiveAttributes);
            var ioResultPromise = new TaskCompletionSource<IOResult>();

            var os = _createOutput();
            var maxInputBufferSize = context
                .EffectiveAttributes
                .GetMandatoryAttribute<Attributes.InputBuffer>()
                .Max;
            var props = OutputStreamSubscriber
                .Props(os, ioResultPromise, maxInputBufferSize, _autoFlush)
                .WithDispatcher(context
                    .EffectiveAttributes
                    .GetMandatoryAttribute<ActorAttributes.Dispatcher>()
                    .Name);
            var actorRef = mat.ActorOf(context, props);

            materializer = ioResultPromise.Task;
            return new ActorSubscriberImpl<ByteString>(actorRef);
        }
    }
}
