using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildFs
{
    public abstract class BuildFsApi : MarshalByRefObject
    {
        public readonly static string IpcChannelName = "ShamanBuildFs";

        public abstract int RunForwarded(string location, string commandLine, string currentDirectory, bool custom);


        public static void ParseCommandLine(string commandLine, out string exe, out string args)
        {
            if (commandLine[0] == '"')
            {
                var end = commandLine.IndexOf('"', 1);
                exe = commandLine.Substring(1, end - 1);
                args = commandLine.Substring(end + 2);
            }
            else
            {
                var end = commandLine.IndexOf(' ');
                if (end == -1)
                {
                    exe = commandLine;
                    args = string.Empty;
                }
                else
                {
                    exe = commandLine.Substring(0, end);
                    args = commandLine.Substring(end + 1);
                }
            }
        }

        public abstract void EnsureConnected();


        public static string FindPath(string file, string workingDirectory)
        {
            if (file.Contains("/") || file.Contains("\\")) return file;
            IEnumerable<string> paths = Environment.GetEnvironmentVariable("path").Split(';').Select(x => Environment.ExpandEnvironmentVariables(x)).ToList();
            if (workingDirectory != null) paths = new[] { workingDirectory }.Concat(paths);
            
            foreach (var p_ in paths)
            {
                var p = p_;
                if (!p.EndsWith("\\")) p += "\\";
                var pfile = p + file;
                if (File.Exists(pfile))
                {
                    var ext = Path.GetExtension(pfile).ToLower();
                    if (ext == ".exe" || ext == ".cmd" || ext == ".bat") return pfile;
                }
                if (File.Exists(pfile + ".exe")) return pfile + ".exe";
                if (File.Exists(pfile + ".cmd")) return pfile + ".cmd";
                if (File.Exists(pfile + ".bat")) return pfile + ".bat";

            }
            throw new FileNotFoundException();
        }

    }
}
