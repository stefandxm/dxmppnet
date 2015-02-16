using System;
using System.Collections.Generic;
using System.IO;

namespace DXMPP.Network
{
	internal class MojsStream : Stream
	{
		LinkedList<byte[]> Data = new LinkedList<byte[]>();


		#region implemented abstract members of Stream
		public override void Flush()
		{
		}
		public override int Read(byte[] buffer, int offset, int count)
		{
            int NrBytesRead = 0;
			bool Sleep = false;
            while( !Stopped && NrBytesRead < count  )
			{
				if (Sleep)
				{
					System.Threading.Thread.Sleep(5);
					Sleep = false;
				}

				lock (Data)
				{
					LinkedListNode<byte[]> SmallData = Data.First;
					if (NrBytesRead == 0 && SmallData == null)
					{
						Sleep = true;
						continue;
					}

					if (SmallData == null)
						break;

					Data.RemoveFirst();

					int BytesToCopyFromThisSegment = Math.Min(SmallData.Value.Length, count - NrBytesRead);
					Buffer.BlockCopy(SmallData.Value, 0, buffer, offset + NrBytesRead, BytesToCopyFromThisSegment);
					NrBytesRead += BytesToCopyFromThisSegment;

					if (BytesToCopyFromThisSegment < SmallData.Value.Length)
					{
						int RemainingByteCount = SmallData.Value.Length - BytesToCopyFromThisSegment;
						byte[] RemainingBytesFromThisSegment = new byte[RemainingByteCount];
						Buffer.BlockCopy(SmallData.Value, BytesToCopyFromThisSegment, RemainingBytesFromThisSegment, 0, RemainingByteCount);
						Data.AddFirst(RemainingBytesFromThisSegment);
					}

                    if (!HasData)
                        break;
				}
			}

			return NrBytesRead;


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
            throw new InvalidOperationException();
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

		public bool HasData
		{
			get
			{
                lock (Data)
                {
                    bool RVal = (Data.First != null);
/*                    if (RVal)
                        Console.WriteLine("{0}Hasdata!", Environment.NewLine);
                    else
                        Console.WriteLine("No data");*/
                    return RVal;
                }
			}
		}

		volatile bool Stopped = false;

		public void Stop()
		{
			Stopped = true;
            lock (Data)
            {
                // Do nothing: Just make sure we are relased
            }
		}

		public void ClearStart()
		{
            lock (Data)
            {
                Data.Clear();
                Stopped = false;
            }
		}

		public void PushData(byte[] NewData)
		{
            lock (Data)
            {
                Data.AddLast(NewData);
            }
		}
	}
}

