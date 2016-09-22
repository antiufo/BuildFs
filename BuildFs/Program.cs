using DokanNet;
using Shaman.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BuildFs
{
    class Program
    {
        static void Main(string[] args)
        {
           // ProcessUtils.RunPassThroughFrom("C:\\temp", "csc.exe", "/?");
            
            var fs = BuildFsFileSystem.Mount('R');
            // TODO: docgen, finaldoc
            fs.AddProject(@"C:\Repositories\Awdee", "awdee");
            RunAwdeeCached(fs, "nuget-restore");
           /* RunAwdeeCached(fs, "confuser-cs");
            RunAwdeeCached(fs, "core-available-location-icons");
            RunAwdeeCached(fs, "core-phantomts");
            RunAwdeeCached(fs, "core-compile");

            // website-all
            RunAwdeeCached(fs, "website-grunt-install");
            RunAwdeeCached(fs, "website-json-css");
            RunAwdeeCached(fs, "website-json-css");
            RunAwdeeCached(fs, "website-copy");
            RunAwdeeCached(fs, "website-grunt");
            RunAwdeeCached(fs, "website-files-folder");
            RunAwdeeCached(fs, "website-restore");
            */
            /*while (true)
            {
                Thread.Sleep(400000);
            }*/

        }

        private static void RunAwdeeCached(BuildFsFileSystem fs, string v)
        {
            fs.RunCached("awdee", "Xamasoft.Awdee.Build", "nmake", v);
        }

        private static void RunAwdeeCached(BuildFsFileSystem fs, params string[] tasks)
        {
            foreach (var item in tasks)
            {
                RunAwdeeCached(fs, item);
            }
            
        }
    }
}
