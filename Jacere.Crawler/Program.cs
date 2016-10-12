using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ManyConsole;

namespace Jacere.Crawler
{
    class Program
    {
        private static int Main(string[] args)
        {
            // keep-alive
            if (NativeMethods.SetThreadExecutionState(
                NativeMethods.ES_CONTINUOUS | NativeMethods.ES_SYSTEM_REQUIRED) == 0)
            {
                throw new Exception("failed to set execution state");
            }

            var commands = GetCommands();
            return ConsoleCommandDispatcher.DispatchCommand(commands, args, Console.Out);
        }

        public static IEnumerable<ConsoleCommand> GetCommands()
        {
            var dir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            var files = Directory.GetFiles(dir, $"{typeof(Program).Namespace}.*.dll");

            return new[]
            {
                Assembly.GetAssembly(typeof(Program)),
            }.Concat(files.Select(Assembly.LoadFile))
                .SelectMany(ConsoleCommandDispatcher.FindCommandsInAssembly);
        }
    }
}
