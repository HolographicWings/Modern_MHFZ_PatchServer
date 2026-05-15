using PatchServer;
using System;
using System.Collections.Generic;
using System.Text;

namespace Modern_MHFZ_PatchServer.commands
{
    internal class CommandDispatcher
    {
        // Processes a command string.
        public static async Task<string> SendCommand(string? command, CommandSource source)
        {
            if (command == null)
                command = string.Empty;

            command = command.Trim();

            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // Implementation for sending a command with arguments
            // TODO: Implement more commands

            if (parts.Length > 0 && parts[0] == "stop")
            {
                if(source == CommandSource.Web)
                {
                    return "Stop command cannot be issued from Admin Panel.";
                }
                await Program.RequestStop();
                return "Stopping requested.";
            }

            return "Unknown command.";
        }
        public enum CommandSource
        {
            Console,
            Web
        }
    }
}
