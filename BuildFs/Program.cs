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

            var fs = BuildFsFileSystem.Mount('R');
            // TODO: docgen, finaldoc

            fs.AddProject(@"C:\Repositories\Awdee", "awdee");

            try
            {

                //RunAwdeeNmake(fs, "nuget-restore");
                RunAwdeeNmake(fs, "confuser-cs");
                RunAwdeeNmake(fs, "core-available-location-icons");
                RunAwdeeNmake(fs, "core-phantomts");
                RunAwdeeNmake(fs, "core-compile");

                //// website-all
                RunAwdeeNmake(fs, "website-css-generator");
                RunAwdeeNmake(fs, "website-grunt-install");
                RunAwdeeNmake(fs, "website-json-css");
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

                RunAwdeeNmake(fs, "core-adblock-update");
                RunAwdeeNmake(fs, "core-compile");
                RunAwdeeNmake(fs, "ws-refasm-copy-orig");
                RunAwdeeNmake(fs, "ws-refasm");
                RunAwdeeNmake(fs, "ws-refasm-shaman");
                if (Debugger.IsAttached)
                    Console.WriteLine("Done.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            
            //while (true) Thread.Sleep(400000);
            
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
