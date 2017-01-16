using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Ipc;
using System.Text;
using System.Threading.Tasks;

namespace BuildFs.Controller
{
    class Program
    {
        static int Main(string[] args)
        {
            BuildFsApi api = null;
            var location = typeof(Program).Assembly.Location;
            var custom = Path.GetFileNameWithoutExtension(location).Equals("buildfs-run", StringComparison.OrdinalIgnoreCase);
            if (custom || !ShouldForceDirect())
            {
                try
                {
                    api = GetBuildFsApi();
                    api.EnsureConnected();
                }
                catch (Exception ex)
                {
                    api = null;
                }

            }

            if (api == null && custom)
            {
                Console.WriteLine("BuildFS is not running.");
                return 1;
            }



            try
            {
                if (api != null)
                {
                    
                    return api.RunForwarded(location, Environment.CommandLine, Environment.CurrentDirectory, custom);
                }
                else
                {
                    string exe;
                    string arguments;
                    
                    BuildFsApi.ParseCommandLine(Environment.CommandLine, out exe, out arguments);

                    var realexe = Path.Combine(Path.GetDirectoryName(location), Path.GetFileNameWithoutExtension(location) + "-real.exe");
                    return RunPassThroughFromSimple(Environment.CurrentDirectory, realexe, arguments);
                }
                
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return 1;
            }

        }

        private static bool ShouldForceDirect()
        {
            var cmdline = Environment.CommandLine;
            if (cmdline.Contains("org.gradle.launcher.daemon.bootstrap.GradleDaemon")) return true;
            if (cmdline.Contains("aapt.exe") && cmdline.EndsWith(" m")) return true;
            return false;
        }

        public static ProcessStartInfo CreateProcessStartInfoSimple(string workingDirectory, string command, string rawArgs)
        {
            var psi = new ProcessStartInfo();
    
            psi.FileName = command;
            psi.WorkingDirectory = workingDirectory;
            psi.Arguments = rawArgs;
            psi.UseShellExecute = false;
            return psi;
        }

        private static int RunPassThroughFromSimple(string workingDirectory, string command, string rawArgs)
        {
            var psi = CreateProcessStartInfoSimple(workingDirectory, command, rawArgs);

            using (var p = System.Diagnostics.Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        internal static BuildFsApi GetBuildFsApi()
        {

            var ipcCh = new IpcChannel();
            ChannelServices.RegisterChannel(ipcCh, false);
            return
                  (BuildFsApi)Activator.GetObject(
                    typeof(BuildFsApi),
                    string.Format("ipc://{0}/{0}", BuildFsApi.IpcChannelName));

        }
    }
}
