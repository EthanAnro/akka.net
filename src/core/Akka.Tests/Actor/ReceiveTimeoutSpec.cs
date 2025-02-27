﻿//-----------------------------------------------------------------------
// <copyright file="ReceiveTimeoutSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Event;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;


namespace Akka.Tests.Actor
{
    public class Tick { }

    public class TransparentTick : INotInfluenceReceiveTimeout { }
    
    public class ReceiveTimeoutSpec : AkkaSpec
    {
        public class TimeoutActor : ActorBase
        {
            private TestLatch _timeoutLatch;

            public TimeoutActor(TestLatch timeoutLatch)
                : this(timeoutLatch, TimeSpan.FromMilliseconds(500))
            {
            }

            public TimeoutActor(TestLatch timeoutLatch, TimeSpan? timeout)
            {
                _timeoutLatch = timeoutLatch;
                Context.SetReceiveTimeout(timeout.GetValueOrDefault());
            }

            protected override bool Receive(object message)
            {
                if (message is ReceiveTimeout)
                {
                    _timeoutLatch.Open();
                    return true;
                }

                if (message is Tick)
                {
                    return true;
                }

                if (message is TransparentTick)
                {
                    return true;
                }

                return false;
            }
        }
        
        public class AsyncTimeoutActor : ReceiveActor
        {
            public AsyncTimeoutActor(TestLatch timeoutLatch)
                : this(timeoutLatch, TimeSpan.FromMilliseconds(500))
            {
            }

            public AsyncTimeoutActor(TestLatch timeoutLatch, TimeSpan? timeout)
            {
                var log = Context.GetLogger();
                
                Context.SetReceiveTimeout(timeout.GetValueOrDefault());

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
#pragma warning disable AK1003
                ReceiveAsync<ReceiveTimeout>(async _ =>
                {
                    log.Info($"Received {nameof(ReceiveTimeout)}");
                    timeoutLatch.Open();
                });

                ReceiveAsync<TransparentTick>(async _ =>
                {
                    log.Info($"Received {nameof(TransparentTick)}");
                });

                ReceiveAsync<Tick>(async _ =>
                {
                    log.Info($"Received {nameof(Tick)}");
                });
#pragma warning restore AK1003
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
            }

        }

        public class TurnOffTimeoutActor : ActorBase
        {
            private TestLatch _timeoutLatch;
            private readonly AtomicCounter _counter;

            public TurnOffTimeoutActor(TestLatch timeoutLatch, AtomicCounter counter)
            {
                _timeoutLatch = timeoutLatch;
                _counter = counter;
                Context.SetReceiveTimeout(TimeSpan.FromMilliseconds(500));
            }

            protected override bool Receive(object message)
            {
                if (message is ReceiveTimeout)
                {
                    _counter.IncrementAndGet();
                    _timeoutLatch.Open();
                    Context.SetReceiveTimeout(null);
                    return true;
                }

                if (message is Tick)
                {
                    return true;
                }

                return false;
            }
        }

        public class NoTimeoutActor : ActorBase
        {
            private TestLatch _timeoutLatch;

            public NoTimeoutActor(TestLatch timeoutLatch)
            {
                _timeoutLatch = timeoutLatch;
            }

            protected override bool Receive(object message)
            {
                if (message is ReceiveTimeout)
                {
                    _timeoutLatch.Open();
                    return true;
                }

                if (message is Tick)
                {
                    return true;
                }

                return false;
            }
        }

        public ReceiveTimeoutSpec(ITestOutputHelper output): base(output)
        {
        }
        
        [Fact]
        public void An_actor_with_receive_timeout_must_get_timeout()
        {
            var timeoutLatch = new TestLatch();
            var timeoutActor = Sys.ActorOf(Props.Create(() => new TimeoutActor(timeoutLatch, TimeSpan.FromMilliseconds(500))));

            timeoutLatch.Ready(TestKitSettings.DefaultTimeout);
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public void An_actor_with_receive_timeout_must_reschedule_timeout_after_regular_receive()
        {
            var timeoutLatch = new TestLatch();
            var timeoutActor = Sys.ActorOf(Props.Create(() => new TimeoutActor(timeoutLatch, TimeSpan.FromMilliseconds(500))));

            timeoutActor.Tell(new Tick());
            timeoutLatch.Ready(TestKitSettings.DefaultTimeout);

            Sys.Stop(timeoutActor);
        }

        [Fact]
        public void An_actor_with_receive_timeout_must_be_able_to_turn_off_timeout_if_desired()
        {
            var count = new AtomicCounter(0);

            var timeoutLatch = new TestLatch();
            var timeoutActor = Sys.ActorOf(Props.Create(() => new TurnOffTimeoutActor(timeoutLatch, count)));

            timeoutActor.Tell(new Tick());
            timeoutLatch.Ready(TestKitSettings.DefaultTimeout);
            count.Current.ShouldBe(1);
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public void An_actor_with_receive_timeout_must_not_receive_timeout_message_when_not_specified()
        {
            var timeoutLatch = new TestLatch();
            var timeoutActor = Sys.ActorOf(Props.Create(() => new NoTimeoutActor(timeoutLatch)));

            Assert.Throws<TimeoutException>(() => timeoutLatch.Ready(TestKitSettings.DefaultTimeout));
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public void An_actor_with_receive_timeout_must_get_timeout_while_receiving_NotInfluenceReceiveTimeout_messages()
        {
            var timeoutLatch = new TestLatch();
            var timeoutActor = Sys.ActorOf(Props.Create(() => new TimeoutActor(timeoutLatch, TimeSpan.FromSeconds(1))));
            
            var cancelable = Sys.Scheduler.Advanced.ScheduleRepeatedlyCancelable(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(100),
                () =>
                {
                    timeoutActor.Tell(new TransparentTick());
                    timeoutActor.Tell(new Identify(null));
                });

            timeoutLatch.Ready(TestKitSettings.DefaultTimeout);
            cancelable.Cancel();
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public void An_async_actor_with_receive_timeout_must_get_timeout_while_receiving_NotInfluenceReceiveTimeout_messages()
        {
            var timeoutLatch = new TestLatch();
            var timeoutActor = Sys.ActorOf(Props.Create(() => new AsyncTimeoutActor(timeoutLatch, TimeSpan.FromSeconds(1))));
            
            var cancelable = Sys.Scheduler.Advanced.ScheduleRepeatedlyCancelable(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(100),
                () =>
                {
                    timeoutActor.Tell(new TransparentTick());
                    //timeoutActor.Tell(new Identify(null));
                });

            timeoutLatch.Ready(TestKitSettings.DefaultTimeout);
            cancelable.Cancel();
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public void An_actor_with_receive_timeout_must_get_timeout_while_receiving_only_NotInfluenceReceiveTimeout_messages()
        {
            var timeoutLatch = new TestLatch(2);

            Action<IActorDsl> actor = d =>
            {
                d.OnPreStart = c => c.SetReceiveTimeout(TimeSpan.FromSeconds(1));
                d.Receive<ReceiveTimeout>((_, c) =>
                {
                    c.Self.Tell(new TransparentTick());
                    timeoutLatch.CountDown();
                });
                d.Receive<TransparentTick>((_, _) => { });
            };
            var timeoutActor = Sys.ActorOf(Props.Create(() => new Act(actor)));

            timeoutLatch.Ready(TestKitSettings.DefaultTimeout);
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public async Task Issue469_An_actor_with_receive_timeout_must_cancel_receive_timeout_when_terminated()
        {
            //This test verifies that bug #469 "ReceiveTimeout isn't cancelled when actor terminates" has been fixed
            var timeoutLatch = CreateTestLatch();
            Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));
            var timeoutActor = Sys.ActorOf(Props.Create(() => new TimeoutActor(timeoutLatch, TimeSpan.FromMilliseconds(500))));

            //make sure TestActor gets a notification when timeoutActor terminates
            Watch(timeoutActor);

            // wait for first ReceiveTimeout message, in which the latch is opened
            timeoutLatch.Ready(TimeSpan.FromSeconds(2));

            //Stop and wait for the actor to terminate
            Sys.Stop(timeoutActor);
            await ExpectTerminatedAsync(timeoutActor);

            //We should not get any messages now. If we get a message now, 
            //it's a DeadLetter with ReceiveTimeout, meaning the receivetimeout wasn't cancelled.
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void An_actor_with_receive_timeout_must_be_able_to_turn_on_timeout_in_NotInfluenceReceiveTimeout_message_handler()
        {
            var timeoutLatch = new TestLatch();

            Action<IActorDsl> actor = d =>
            {
                d.Receive<TransparentTick>((_, c) => c.SetReceiveTimeout(500.Milliseconds()));
                d.Receive<ReceiveTimeout>((_, _) => timeoutLatch.Open());
            };
            var timeoutActor = Sys.ActorOf(Props.Create(() => new Act(actor)));
            timeoutActor.Tell(new TransparentTick());

            timeoutLatch.Ready(TestKitSettings.DefaultTimeout);
            Sys.Stop(timeoutActor);
        }
        
        [Fact]
        public void An_actor_with_receive_timeout_must_be_able_to_turn_off_timeout_in_NotInfluenceReceiveTimeout_message_handler()
        {
            var timeoutLatch = new TestLatch();

            Action<IActorDsl> actor = d =>
            {
                d.OnPreStart = c => c.SetReceiveTimeout(500.Milliseconds());
                d.Receive<TransparentTick>((_, c) => c.SetReceiveTimeout(null));
                d.Receive<ReceiveTimeout>((_, _) => timeoutLatch.Open());
            };
            var timeoutActor = Sys.ActorOf(Props.Create(() => new Act(actor)));
            timeoutActor.Tell(new TransparentTick());

            Assert.Throws<TimeoutException>(() => timeoutLatch.Ready(1.Seconds()));
            Sys.Stop(timeoutActor);
        }
    }
}

