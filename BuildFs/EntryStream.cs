using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BuildFs
{
    class EntryStream : Stream
    {
        private int position;
        private Entry entry;
        private FileAccess fileAccess;
        public EntryStream(Entry entry, FileAccess fileAccess)
        {
            this.entry = entry;
            this.fileAccess = fileAccess;
        }
        private byte[] ContentsArray => entry.Contents.GetBuffer();
        public override bool CanRead
        {
            get
            {
                return fileAccess != FileAccess.Write;
            }
        }

        public override bool CanSeek
        {
            get
            {
                return true;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return fileAccess != FileAccess.Read;
            }
        }

        public override long Length
        {
            get
            {
                return entry.Length;
            }
        }

        public override long Position
        {
            get
            {
                return position;
            }

            set
            {
                position = checked((int)value);
            }
        }

        public override void Flush()
        {
        }


        

        public override int Read(byte[] buffer, int offset, int count)
        {
            var copied = Math.Min(count, (int)Length - position);
            if (copied < 0) copied = 0;
            Buffer.BlockCopy(ContentsArray, position, buffer, offset, copied);
            return copied;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var off = checked((int)offset);
            switch (origin)
            {
                case SeekOrigin.Begin: return position = off;
                case SeekOrigin.Current: return position += off;
                case SeekOrigin.End: return Length + off;
                default: throw new NotSupportedException();
            }
        }

        public override void SetLength(long value)
        {
            entry.Contents.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            entry.Contents.Seek(position, SeekOrigin.Begin);
            entry.Contents.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            var ent = Interlocked.Exchange(ref entry, null);
            if (ent != null)
            {
                ent.FileSystem.ReleaseOneHandle(ent);
            }
            base.Dispose(disposing);
        }
    }
}
