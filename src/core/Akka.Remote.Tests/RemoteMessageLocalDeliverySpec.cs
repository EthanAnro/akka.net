﻿//-----------------------------------------------------------------------
// <copyright file="RemoteMessageLocalDeliverySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.TestKit.TestActors;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests
{
    /// <summary>
    /// Came across some issues while debugging multi-node tests which indicated
    /// that the <see cref="RemoteActorRefProvider"/> couldn't successfully decode full addresses
    /// for local actors, such as "akka.trttl.gremlin.tcp://AttemptSysMsgRedeliverySpec@localhost:57512/user/echo",
    /// into valid <see cref="LocalActorRef"/>s.
    /// 
    /// This spec is designed to verify that these types of paths, including ones with transport adapters at the front, 
    /// can be successfully translated.
    /// </summary>
    public class RemoteMessageLocalDeliverySpec : AkkaSpec
    {
        private static readonly Config RemoteConfiguration = ConfigurationFactory.ParseString(@"
            akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                akka.remote.dot-netty.tcp.hostname = 127.0.0.1
                akka.remote.dot-netty.tcp.port = 0
            akka.remote.dot-netty.tcp.applied-adapters = [trttl, gremlin]
        ");

        public RemoteMessageLocalDeliverySpec(ITestOutputHelper output) : base(RemoteConfiguration, output) { }

        /// <summary>
        /// Reproduces https://github.com/akkadotnet/akka.net/issues/2151
        /// </summary>
        [Fact]
        public void RemoteActorRefProvider_should_create_RemoteActorRef_for_nonlocaladdress()
        {
            var nonLocalAddress = new Address("akka.trttl.gremlin.tcp", Sys.Name, "localhost", RARP.For(Sys).Provider.DefaultAddress.Port);
            var nonLocalActorPath = new RootActorPath(nonLocalAddress) / "user" / "foo";
            var resolved = RARP.For(Sys).Provider.ResolveActorRefWithLocalAddress(nonLocalActorPath.ToSerializationFormat(), nonLocalAddress);
            Assert.IsType<RemoteActorRef>(resolved); // should be a remote actorref
        }

        [Fact]
        public void RemoteActorRefProvider_default_address_must_include_adapter_schemes()
        {
            var localAddress = RARP.For(Sys).Provider.DefaultAddress;
            Assert.StartsWith("akka.trttl.gremlin.tcp://", localAddress.ToString());
        }

        [Fact]
        public void RemoteActorRefProvider_should_correctly_resolve_valid_LocalActorRef_from_remote_address()
        {
            var actorRef = Sys.ActorOf(BlackHoleActor.Props, "myActor");
            var localAddress = RARP.For(Sys).Provider.DefaultAddress;
            var actorPath = new RootActorPath(localAddress) / "user"  / "myActor";

            var resolvedActorRef = RARP.For(Sys).Provider.ResolveActorRefWithLocalAddress(actorPath.ToStringWithAddress(), localAddress);
            Assert.Equal(actorRef, resolvedActorRef);
        }

        [Fact]
        public async Task RemoteActorRefProvider_should_correctly_resolve_valid_LocalActorRef_from_second_remote_system()
        {
            var sys2 = ActorSystem.Create("Sys2", RemoteConfiguration);
            try
            {
                await WithinAsync(TimeSpan.FromSeconds(15), async () =>
                {
                    var actorRef = sys2.ActorOf(BlackHoleActor.Props, "myActor");
                    var sys2Address = RARP.For(sys2).Provider.DefaultAddress;
                    var actorPath = new RootActorPath(sys2Address) / "user" / "myActor";

                    // get a remoteactorref for the second system
                    var remoteActorRef = await Sys.ActorSelection(actorPath).ResolveOne(TimeSpan.FromSeconds(3));

                    // disconnect us from the second actorsystem
                    Assert.True(await RARP.For(Sys)
                        .Provider.Transport.ManagementCommand(new SetThrottle(sys2Address,
                            ThrottleTransportAdapter.Direction.Both, Blackhole.Instance))
                        .WithTimeout(TimeSpan.FromSeconds(3)));

                    // start deathwatch (won't be delivered initially)
                    Watch(remoteActorRef);
                    await Task.Delay(TimeSpan.FromSeconds(3)); // if we delay the initial send, this spec will fail

                    Assert.True(await RARP.For(Sys)
                        .Provider.Transport.ManagementCommand(new SetThrottle(sys2Address,
                            ThrottleTransportAdapter.Direction.Both, Unthrottled.Instance))
                        .WithTimeout(TimeSpan.FromSeconds(3)));

                    // fire off another non-system message
                    var ai = 
                        await Sys.ActorSelection(actorPath).Ask<ActorIdentity>(new Identify(null), TimeSpan.FromSeconds(3));

                    remoteActorRef.Tell(PoisonPill.Instance); // WATCH should be applied first
                    await ExpectTerminatedAsync(remoteActorRef);
                });
            }
            finally
            {
                Shutdown(sys2);
            }
        }
    }
}
