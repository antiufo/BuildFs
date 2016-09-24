using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DokanNet;

namespace BuildFs
{
    class Entry
    {
        public FileAttributes Attributes;
        public DateTime LastWriteTimeUtc;
        public MemoryStream Contents;
        public long Length => Contents.Length;

        internal HashSet<string> Items;
        internal int OpenHandles;
        internal CacheFs FileSystem;
    }
}
