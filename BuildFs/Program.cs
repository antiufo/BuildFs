using DokanNet;
using Microsoft.Win32.SafeHandles;
using Shaman.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Ipc;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BuildFs
{
    partial class Program
    {

        static void Main(string[] args)
        {

            var letter = 'R';

            var chan = new IpcChannel(BuildFs.BuildFsApi.IpcChannelName);
            ChannelServices.RegisterChannel(chan, false);
            RemotingConfiguration.RegisterWellKnownServiceType(typeof(BuildFsApiImpl), BuildFsApi.IpcChannelName, WellKnownObjectMode.Singleton);

            var fs = BuildFsFileSystem.Mount(letter, DokanOptions.FixedDrive);
            //fs.ForceRun = true;
            BuildFsApiImpl.Run = (location, commandLine, folder, custom) =>
            {
                try
                {
                    if (custom)
                    {
                        folder = Path.GetFullPath(folder);
                        var components = folder.SplitFast('\\', StringSplitOptions.RemoveEmptyEntries);
                        if (components[0].ToUpper() != letter + ":") throw new Exception("Must be run in BuildFs drive.");

                        string exe;
                        string arguments;
                        BuildFsApi.ParseCommandLine(commandLine, out exe, out arguments);
                        commandLine = arguments;
                        BuildFsApi.ParseCommandLine(commandLine, out exe, out arguments);

                        fs.RunCached(components[1], string.Join("\\", components.Skip(2)), exe, arguments);
                    }
                    else
                    {

                        var projname = "project";
                        var find = @"C:\Path\To\Project";
                        var replace = letter + @":\" + projname;
                        string exe;
                        string arguments;
                        folder = folder.Replace(find, replace);
                        commandLine = commandLine.Replace(find, replace);
                        BuildFsApi.ParseCommandLine(commandLine, out exe, out arguments);

                        var realexe = Path.Combine(Path.GetDirectoryName(location), Path.GetFileNameWithoutExtension(location) + "-real.exe");
                        fs.RunCached(projname, folder, realexe, new ProcessUtils.RawCommandLineArgument(arguments));
                    }
                    Console.WriteLine("Success.");
                    return 0;
                }
                catch (ProcessException ex)
                {
                    Console.WriteLine("Failed with " + ex.ExitCode);
                    return ex.ExitCode;
                }
            };


            //MainInternal(args, fs);


            // Example:

            //fs.AddProject(@"C:\Path\To\Project", "project");
            //fs.RunCached("project", "subdir", "nmake", "part-1");
            //fs.RunCached("project", "subdir", "nmake", "part-2");



            while (true) Thread.Sleep(400000);

        }




    }
}
