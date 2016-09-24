using DokanNet;
using Microsoft.Win32.SafeHandles;
using Shaman.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BuildFs
{
    class Program
    {

        static void Main(string[] args)
        {
#if false
            var cache = CacheFs.Mount('Q');
            Task.Run(() =>
            {
                while (true)
                {
                    Thread.Sleep(5000);
                    var usage = cache.GetInMemoryUsage() / 1024 / 1024;
                    Console.WriteLine("Usage: " + usage + " MB");
                }
            });

            Task.Run(() =>
            {
                while (true)
                {
                    Thread.Sleep(2 * 60 * 1000);
                    Console.WriteLine("DeleteInMemoryFiles");
                    cache.DeleteInMemoryFiles((path, entry) => entry.OpenHandles == 0 && path.IndexOf("cache2\\entries", StringComparison.OrdinalIgnoreCase) != -1, 60 * 1024 * 1024);
                }
            });

            Task.Run(() =>
            {
                while (true)
                {
                    Thread.Sleep(3 * 60 * 1000);
                    Console.WriteLine("SaveChanges");
                    cache.SaveChanges((path, entry) => entry.OpenHandles == 0 && path.IndexOf("cache2\\entries", StringComparison.OrdinalIgnoreCase) != -1);
                }
            });

            while (true) Thread.Sleep(1000000);
            return;
#endif


            // TODO: docgen, finaldoc
            var fs = BuildFsFileSystem.Mount('R', DokanOptions.FixedDrive);
            fs.AddProject(@"C:\Repositories\Awdee", "awdee");
            if (true)
            {
                try
                {

                    //RunAwdeeNmake(fs, "nuget-restore");
                    RunAwdeeNmake(fs, "confuser-cs");
                    RunAwdeeNmake(fs, "core-available-location-icons");
                    RunAwdeeNmake(fs, "core-adblock-update");
                    RunAwdeeNmake(fs, "core-phantomts");
                    RunAwdeeNmake(fs, "core-compile");
                    RunAwdeeNmake(fs, "service-models-compile");

                    RunAwdeeNmake(fs, "website-css-generator");
                    RunAwdeeNmake(fs, "website-grunt-install");
                    RunAwdeeNmake(fs, "website-copyjs");
                    RunAwdeeGrunt(fs, "less");
                    RunAwdeeGrunt(fs, "typescript:base");
                    RunAwdeeGrunt(fs, "uglify:editor");
                    RunAwdeeGrunt(fs, "uglify:offload");
                    RunAwdeeGrunt(fs, "uglify:admin");
                    RunAwdeeGrunt(fs, "uglify:explore");
                    RunAwdeeGrunt(fs, "uglify:ace");
                    RunAwdeeGrunt(fs, "uglify:explorehtml");
                    RunAwdeeNmake(fs, "website-files-folder");
                    RunAwdeeNmake(fs, "website-restore");
                    RunAwdeeNmake(fs, "website-dlls");

                    RunAwdeeNmake(fs, "ws-refasm-copy-orig");
                    RunAwdeeNmake(fs, "ws-refasm");
                    RunAwdeeNmake(fs, "ws-refasm-shaman");
                    Console.WriteLine("Done.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            while (true) Thread.Sleep(400000);

        }

        private static void RunAwdeeGrunt(BuildFsFileSystem fs, string name)
        {
            RunAwdee(fs, "Xamasoft.Awdee.WebSite", "cmd", "/c", "grunt", name);
        }

        private static void RunAwdeeNmake(BuildFsFileSystem fs, string v)
        {
            RunAwdee(fs, "Xamasoft.Awdee.Build", "nmake", v);
        }

        private static void RunAwdee(BuildFsFileSystem fs, string folder, string v, params object[] args)
        {
            fs.RunCached("awdee", folder, v, args);
        }


    }
}
