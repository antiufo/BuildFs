using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildFs
{
    

    class ItemPair
    {
        public ItemStatus Before;
        public ItemStatus After;
        public bool Changed;
    }

    [ProtoContract]
    class ItemStatus
    {
        [ProtoMember(1)]
        public DateTime LastWriteTime;
        [ProtoMember(2)]
        public long Size;
        [ProtoMember(3)]
        public FileAttributes Attributes;
        [ProtoIgnore]
        public byte[] ContentsHash;
        [ProtoMember(4)]
        public string Path;
    }

    [ProtoContract]
    class ExecutionSummary
    {
        [ProtoMember(1)]
        public List<ItemStatus> Inputs;
        [ProtoMember(2)]
        public List<string> Outputs;
        [ProtoMember(3)]
        public bool Available;
    }
}
