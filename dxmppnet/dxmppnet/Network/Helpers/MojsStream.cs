using System;
using System.IO;

namespace DXMPP.Network
{
    internal class MojsStream : Stream 
    {
        System.Collections.Concurrent.ConcurrentQueue<byte> Data = 
            new System.Collections.Concurrent.ConcurrentQueue<byte>();

        #region implemented abstract members of Stream
        public override void Flush()
        {
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                byte SmallData;
                /*if (i == 0)
                {
                    // Block
                    while (!Data.TryDequeue(out SmallData))
                    {
                        // Block
                    }
                }
                else*/
                {
                    if(!Data.TryDequeue(out SmallData))
                        return i;
                }

                buffer[i + offset] = SmallData;
            }

            return count;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new InvalidOperationException();
        }

        public override void SetLength(long value)
        {
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override bool CanRead
        {
            get
            {
                return true;
            }
        }
        public override bool CanSeek
        {
            get
            {
                return false;
            }
        }
        public override bool CanWrite
        {
            get
            {
                return false;
            }
        }
        public override long Length
        {
            get
            {
                return 1;
            }
        }
        public override long Position
        {
            get
            {
                return 1;
            }
            set
            {
                throw new InvalidOperationException();
            }
        }
        #endregion

        public void PushStringData( string NewData )
        {
            byte[] RawData = System.Text.UTF8Encoding.Default.GetBytes(NewData);

            foreach (byte SmallData in RawData)
            {
                Data.Enqueue(SmallData);
            }
        }
    }
}

