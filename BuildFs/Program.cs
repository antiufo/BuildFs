using DokanNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildFs
{
    class Program
    {
        static void Main(string[] args)
        {
            Dokan.Unmount('R');
            var fs = new BuildFsFileSystem();
            fs.AddProject(@"C:\temp\-buildfstest\proj", "proj");
            //fs.AddProject(@"C:\Repositories\Awdee", "awdee");
            Dokan.Mount(fs, "R:", DokanOptions.DebugMode/* | DokanOptions.StderrOutput*/, 1);
        }
    }
}
