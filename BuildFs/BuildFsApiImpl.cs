using System;

namespace BuildFs
{
    internal class BuildFsApiImpl : BuildFsApi
    {
        public static Func<string, string, string, bool, int> Run;

        public override void EnsureConnected()
        {
        }

        public override int RunForwarded(string location, string commandLine, string currentDirectory, bool custom)
        {
            Console.WriteLine("Executing: " + commandLine);
            Run(location, commandLine, currentDirectory, custom);
            return 0;
        }
    }
}