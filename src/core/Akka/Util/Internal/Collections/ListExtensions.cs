﻿//-----------------------------------------------------------------------
// <copyright file="ListExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akka.Util.Internal.Collections
{
    internal static class ListExtensions
    {
        public static List<T> Shuffle<T>(this List<T> @this)
        {
            var list = new List<T>(@this);
            var r = ThreadLocalRandom.Current;
            for (int i = list.Count - 1; i > 0; i--)
            {
                int index = r.Next(i);
                //swap
                var tmp = list[index];
                list[index] = list[i];
                list[i] = tmp;
            }
            return list;
        }

        public static IImmutableList<T> Shuffle<T>(this IImmutableList<T> @this)
        {
            var list = ImmutableList.CreateBuilder<T>();
            list.AddRange(@this);

            var r = ThreadLocalRandom.Current;
            for (int i = list.Count - 1; i > 0; i--)
            {
                int index = r.Next(i);
                //swap
                var tmp = list[index];
                list[index] = list[i];
                list[i] = tmp;
            }
            return list.ToImmutable();
        }
    }
}
