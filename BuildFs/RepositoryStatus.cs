using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildFs
{
    class RepositoryStatus
    {
        public Dictionary<string, ItemPair> Items;


    }

    class ItemPair
    {
        public ItemStatus Before;
        public ItemStatus After;
        public bool Changed;
    }

    class ItemStatus
    {
        public DateTime LastWriteTime;
        public long Size;
        public FileAttributes Attributes;
    }
}
