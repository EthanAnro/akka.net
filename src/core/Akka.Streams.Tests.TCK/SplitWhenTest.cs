﻿//-----------------------------------------------------------------------
// <copyright file="SplitWhenTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    class SplitWhenTest : AkkaPublisherVerification<int>
    {
        public override IPublisher<int> CreatePublisher(long elements)
        {
            if (elements == 0)
                return EmptyPublisher<int>.Instance;


            var flow = (Source<Source<int, NotUsed>, NotUsed>)
                Source.From(Enumerate(elements))
                    .SplitWhen(_ => false)
                    .PrefixAndTail(0)
                    .Select(tuple => tuple.Item2)
                    .ConcatSubstream();

            var futureSource = flow.RunWith(Sink.First<Source<int, NotUsed>>(), Materializer);
            var source = futureSource.Result;

            return source.RunWith(Sink.AsPublisher<int>(false), Materializer);
        }
    }
}
