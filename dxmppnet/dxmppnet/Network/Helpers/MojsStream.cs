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
			int i = 0;
			bool Sleep = false;
			for ( /* no */ ; i < count && !Stopped;)
			{
				if (Sleep)
				{
					System.Threading.Thread.Sleep(5);
					Sleep = false;
				}

				lock (Data)
				{
					LinkedListNode<byte[]> SmallData = Data.First;
					if (i == 0 && SmallData == null)
					{
						Sleep = true;
						continue;
					}

					if (SmallData == null)
						break;

					Data.RemoveFirst();

					int BytesToCopyFromThisSegment = Math.Min(SmallData.Value.Length, count - i);
					Buffer.BlockCopy(SmallData.Value, 0, buffer, offset + i, BytesToCopyFromThisSegment);
					i += BytesToCopyFromThisSegment;

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
			//Console.WriteLine("Reading. Requested offset: {0}, count: {1}, returned: {2}, text: {3}", 
			//	offset, count, i, System.Text.Encoding.UTF8.GetString(buffer));

			return i;


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

		public bool HasData
		{
			get
			{
				lock (Data)
					return (Data.First != null);
			}
		}

		volatile bool Stopped = false;

		public void Stop()
		{
			Stopped = true;
		}

		public void ClearStart()
		{
			lock (Data)
				Data.Clear();
			Stopped = false;
		}

		public void PushStringData(byte[] NewData)
		{
			lock (Data)
				Data.AddLast(NewData);
		}
	}
}

