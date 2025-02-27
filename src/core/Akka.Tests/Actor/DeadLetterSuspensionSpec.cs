﻿//-----------------------------------------------------------------------
// <copyright file="DeadLetterSuspensionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class DeadLetterSuspensionSpec : AkkaSpec
    {
        private class Dropping : ActorBase
        {
            public static Props Props() => Akka.Actor.Props.Create(() => new Dropping());

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case int n:
                        Context.System.EventStream.Publish(new Dropped(n, "Don't like numbers", Self));
                        return true;
                }
                return false;
            }
        }

        private class Unandled : ActorBase
        {
            public static Props Props() => Akka.Actor.Props.Create(() => new Unandled());

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case int n:
                        Unhandled(n);
                        return true;
                }
                return false;
            }
        }

        private static readonly Config Config = ConfigurationFactory.ParseString(@"
            akka.loglevel = INFO
            akka.log-dead-letters = 4
            akka.log-dead-letters-suspend-duration = 2s");

        private readonly IActorRef _deadActor;
        private readonly IActorRef _droppingActor;
        private readonly IActorRef _unhandledActor;

        public DeadLetterSuspensionSpec()
            : base(Config)
        {
            _deadActor = Sys.ActorOf(Props.Create<TestKit.TestActors.EchoActor>());
            Watch(_deadActor);
            _deadActor.Tell(PoisonPill.Instance);
            ExpectTerminated(_deadActor);

            _droppingActor = Sys.ActorOf(Dropping.Props(), "droppingActor");
            _unhandledActor = Sys.ActorOf(Unandled.Props(), "unhandledActor");
        }

        private string ExpectedDeadLettersLogMessage(int count) =>
            $"Message [{count.GetType().Name}] from {TestActor} to {_deadActor} was not delivered. [{count}] dead letters encountered";

        private string ExpectedDroppedLogMessage(int count) =>
            $"Message [{count.GetType().Name}] to {_droppingActor} was dropped. Don't like numbers. [{count}] dead letters encountered";

        private string ExpectedUnhandledLogMessage(int count) =>
            $"Message [{count.GetType().Name}] from {TestActor} to {_unhandledActor} was unhandled. [{count}] dead letters encountered";


        [Fact]
        public async Task Must_suspend_dead_letters_logging_when_reaching_akka_log_dead_letters_and_then_re_enable()
        {
            await EventFilter
                .Info(start: ExpectedDeadLettersLogMessage(1))
                .ExpectAsync(1, () => { _deadActor.Tell(1); return Task.CompletedTask; });

            await EventFilter
                .Info(start: ExpectedDroppedLogMessage(2))
                .ExpectAsync(1, () => { _droppingActor.Tell(2); return Task.CompletedTask; });

            await EventFilter
                .Info(start: ExpectedUnhandledLogMessage(3))
                .ExpectAsync(1, () => { _unhandledActor.Tell(3); return Task.CompletedTask; });

            await EventFilter
                .Info(start: ExpectedDeadLettersLogMessage(4) + ", no more dead letters will be logged in next")
                .ExpectAsync(1, () => { _deadActor.Tell(4); return Task.CompletedTask; });
            _deadActor.Tell(5);
            _droppingActor.Tell(6);

            // let suspend-duration elapse
            await Task.Delay(2050);

            // re-enabled
            await EventFilter
                .Info(start: ExpectedDeadLettersLogMessage(7) + ", of which 2 were not logged")
                .ExpectAsync(1, () => { _deadActor.Tell(7); return Task.CompletedTask; });

            // reset count
            await EventFilter
                .Info(start: ExpectedDeadLettersLogMessage(1))
                .ExpectAsync(1, () => { _deadActor.Tell(8); return Task.CompletedTask; });
        }
    }
}
