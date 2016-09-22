using Shaman.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildFs
{
    class Project
    {
        internal string Path;

        public Dictionary<string, ItemPair> Items;
        public HashSet<string> readFiles = new HashSet<string>();
        public HashSet<string> changedFiles = new HashSet<string>();

        internal string Name;
    }
}
