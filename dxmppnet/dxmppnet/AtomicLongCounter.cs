using System.Threading;

namespace DXMPP
{
    public class AtomicLongCounter 
    {
        private long Value;
        internal void Add(long Increment)
        {
            Interlocked.Add(ref Value, Increment);
        }
        internal void Inc1()
        {
            Interlocked.Increment(ref Value);
        }
        public long Read()
        {
            return Interlocked.Read(ref Value);
        }
    }
}
