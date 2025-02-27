﻿//-----------------------------------------------------------------------
// <copyright file="UdpListener.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Sockets;
using Akka.Actor;
using Akka.Annotations;
using Akka.Dispatch;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.IO
{
    using static Udp;
    using ByteBuffer = ArraySegment<byte>;

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    internal class UdpListener : WithUdpSend, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly IActorRef _bindCommander;
        private readonly Bind _bind;
        protected readonly ILoggingAdapter Log = Context.GetLogger();

        public UdpListener(UdpExt udp, IActorRef bindCommander, Bind bind)
        {
            Udp = udp;
            _bindCommander = bindCommander;
            _bind = bind;

            Context.Watch(bind.Handler);        // sign death pact

            Socket = (bind.Options.OfType<Inet.DatagramChannelCreator>().FirstOrDefault() ??
                      new Inet.DatagramChannelCreator()).Create(bind.LocalAddress.AddressFamily);
            Socket.Blocking = false;
            
            try
            {
                foreach (var option in bind.Options)
                {
                    option.BeforeDatagramBind(Socket);
                }

                Socket.Bind(bind.LocalAddress);
                var ret = Socket.LocalEndPoint;
                if (ret == null)
                    throw new ArgumentException($"bound to unknown SocketAddress [{Socket.LocalEndPoint}]");
                
                Log.Debug("Successfully bound to [{0}]", ret);
                bind.Options.OfType<Inet.SocketOptionV2>().ForEach(x => x.AfterBind(Socket));

                ReceiveAsync();
            }
            catch (Exception e)
            {
                bindCommander.Tell(new CommandFailed(bind));
                Log.Error(e, "Failed to bind UDP channel to endpoint [{0}]", bind.LocalAddress);
                Context.Stop(Self);
            }
        }

        protected sealed override Socket Socket { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected sealed override UdpExt Udp { get; }

        protected override void PreStart()
        {
            _bindCommander.Tell(new Bound(Socket.LocalEndPoint));
            Context.Become(m => ReadHandlers(m) || SendHandlers(m));
        }

        protected override bool Receive(object message)
        {
            throw new NotSupportedException();
        }

        private bool ReadHandlers(object message)
        {
            switch (message)
            {
                case SuspendReading _:
                    // TODO: What should we do here - we cant cancel a pending ReceiveAsync
                    return true;
                case ResumeReading _:
                    ReceiveAsync();
                    return true;
                case SocketReceived received:
                    DoReceive(received, _bind.Handler);
                    return true;
                case Unbind _:
                    Log.Debug("Unbinding endpoint [{0}]", _bind.LocalAddress);
                    try
                    {
                        Socket.Dispose();
                        Sender.Tell(Unbound.Instance);
                        Log.Debug("Unbound endpoint [{0}], stopping listener", _bind.LocalAddress);
                    }
                    finally
                    {
                        Context.Stop(Self);
                    }
                    return true;
            }

            return false;
        }

        private void DoReceive(SocketReceived e, IActorRef handler)
        {
            if(e.IsIcmpError)
            {
                Log.Debug("Ignoring client connection reset.");
                ReceiveAsync();
                return;
            }

            if (e.SocketError != SocketError.Success)
                throw new SocketException((int)e.SocketError);

            handler.Tell(new Received(e.Data, e.RemoteEndPoint));
            ReceiveAsync();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            if (Socket.Connected)
            {
                Log.Debug("Closing DatagramChannel after being stopped");
                try
                {
                    Socket.Dispose();
                }
                catch (Exception e)
                {
                    Log.Debug("Error closing DatagramChannel: {0}", e);
                }
            }
        }
        
        private void ReceiveAsync()
        {
            var e = Udp.SocketEventArgsPool.Acquire(Self);
            e.RemoteEndPoint = Socket.LocalEndPoint;
            if (!Socket.ReceiveFromAsync(e))
            {
                Self.Tell(new SocketReceived(e));
                Udp.SocketEventArgsPool.Release(e);
            }
        }
    }
}
