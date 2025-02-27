﻿//-----------------------------------------------------------------------
// <copyright file="SqlSnapshotConnectionFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sql.TestKit
{
    public abstract class SqlSnapshotConnectionFailureSpec : Akka.TestKit.Xunit2.TestKit
    {
        protected static readonly string DefaultInvalidConnectionString = "INVALID_CONNECTION_STRING";

        public SqlSnapshotConnectionFailureSpec(Config config = null, ITestOutputHelper output = null) : base(config)
        {
        }

        [Fact]
        public void Persistent_actor_should_throw_exception_upon_connection_failure_when_saving_snapshot()
        {
            EventFilter.Exception<Exception>().ExpectOne(() =>
            {
                var pref = Sys.ActorOf(Props.Create(() => new SaveSnapshotTestActor("test-snapshot-actor", TestActor)));
                pref.Tell(TakeSnapshot.Instance);
            });

            ExpectNoMsg();
        }

        // Borrowed from Akka.Persistence.Tests.SnapshotSpec
        private class SaveSnapshotTestActor : NamedPersistentActor
        {
            private readonly IActorRef _probe;
            protected LinkedList<string> _state = new();

            public SaveSnapshotTestActor(string name, IActorRef probe)
                : base(name)
            {
                _probe = probe;
            }

            protected override bool ReceiveRecover(object message)
            {
                switch (message)
                {
                    case SnapshotOffer offer:
                        _state = (LinkedList<string>)offer.Snapshot;
                        return true;
                    case string m:
                        _state.AddFirst(m + "-" + LastSequenceNr);
                        return true;
                    default:
                        return false;
                }
            }

            protected override bool ReceiveCommand(object message)
            {
                switch (message)
                {
                    case string payload:
                        Persist(payload, _ => _state.AddFirst(payload + "-" + LastSequenceNr));
                        return true;
                    case TakeSnapshot _:
                        SaveSnapshot(_state);
                        return true;
                    case SaveSnapshotSuccess s:
                        _probe.Tell(s.Metadata.SequenceNr);
                        return true;
                    case GetState _:
                        _probe.Tell(_state.Reverse().ToArray());
                        return true;
                    default:
                        return false;
                }
            }
        }

        internal class TakeSnapshot
        {
            public static readonly TakeSnapshot Instance = new();
            private TakeSnapshot()
            {
            }
        }

        internal sealed class GetState
        {
            public static readonly GetState Instance = new();
            private GetState() { }
        }

        public abstract class NamedPersistentActor : PersistentActor
        {
            private readonly string _name;

            protected NamedPersistentActor(string name)
            {
                _name = name;
            }

            public override string PersistenceId
            {
                get { return _name; }
            }
        }
    }
}
