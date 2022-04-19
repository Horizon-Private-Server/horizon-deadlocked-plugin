using Horizon.Plugin.Deadlocked.ChatCommands;
using RT.Models.Misc;
using Server.Medius.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked
{
    public static class Chat
    {
        private static readonly BaseChatCommand[] _commands = new BaseChatCommand[]
        {
            new RollChatCommand(),
            new TeamChatCommand()
        };

        public static Task OnChatMessage(ClientObject client, IMediusChatMessage message)
        {
            if (client == null || message.MessageType != RT.Common.MediusChatMessageType.Broadcast || String.IsNullOrEmpty(message.Message))
                return Task.CompletedTask;

            var args = message.Message.Substring(1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!args[0].StartsWith("!"))
                return Task.CompletedTask;

            var commandStr = args[0].Substring(1);
            var command = _commands.FirstOrDefault(x => x.Command == commandStr);
            if (command != null)
            {
                return command.Run(client, args.Skip(1).ToArray());
            }

            return Task.CompletedTask;
        }
    }
}
