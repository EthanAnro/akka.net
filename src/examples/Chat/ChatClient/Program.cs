﻿//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using ChatMessages;

namespace ChatClient
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            var config = ConfigurationFactory.ParseString(@"
akka {  
    actor {
        provider = remote
    }
    remote {
        dot-netty.tcp {
		    port = 0
		    hostname = localhost
        }
    }
}
");

            var system = ActorSystem.Create("MyClient", config);
            var chatClient = system.ActorOf(Props.Create<ChatClientActor>());
            chatClient.Tell(new ConnectRequest("Roggan"));

            while (true)
            {
                var input = Console.ReadLine();
                if (input.StartsWith('/'))
                {
                    var parts = input.Split(' ');
                    var cmd = parts[0].ToLowerInvariant();
                    var rest = string.Join(" ", parts.Skip(1));

                    if (cmd == "/nick")
                    {
                        chatClient.Tell(new ChatClientActor.RequestNewNick(rest));
                    }

                    if (cmd == "/exit")
                    {
                        Console.WriteLine("exiting");
                        break;
                    }
                }
                else
                {
                    chatClient.Tell(new ChatClientActor.PushNewChatMessage(input));
                }
            }

            await system.Terminate();
        }
    }

    internal class ChatClientActor : ReceiveActor, ILogReceive
    {
        private string _nick = "Roggan";

        private readonly ActorSelection _server =
            Context.ActorSelection("akka.tcp://MyServer@localhost:8081/user/ChatServer");

        public record RequestNewNick(string NewNick);

        public record PushNewChatMessage(string Message);

        public ChatClientActor()
        {
            Receive<ConnectRequest>(cr =>
            {
                Console.WriteLine("Connecting....");
                _server.Tell(cr);
            });

            Receive<ConnectResponse>(rsp =>
            {
                Console.WriteLine("Connected!");
                Console.WriteLine(rsp.Message);
            });

            Receive<RequestNewNick>(nr =>
            {
                Console.WriteLine("Changing nick to {0}", nr.NewNick);
                var request = new NickRequest(_nick, nr.NewNick);
                _nick = nr.NewNick;
                _server.Tell(request);
            });

            Receive<NickResponse>(nrsp =>
            {
                Console.WriteLine("{0} is now known as {1}", nrsp.OldUsername, nrsp.NewUsername);
            });

            Receive<PushNewChatMessage>(sr =>
            {
                _server.Tell(new SayRequest(_nick, sr.Message));
            });

            Receive<SayResponse>(srsp => { Console.WriteLine("{0}: {1}", srsp.Username, srsp.Text); });
        }
    }
}
