﻿//-----------------------------------------------------------------------
// <copyright file="EmptyActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Tests.Serialization
{
    public class EmptyActor : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            Context.System.EventStream.Publish(Sender);
        }
    }
}
