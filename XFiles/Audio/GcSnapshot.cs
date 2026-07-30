using System;

namespace XFiles.Audio
{
    internal struct GcSnapshot
    {
        private int _gen0, _gen1, _gen2;
        private long _mem, _alloc;

        public static GcSnapshot Take()
        {
            return new GcSnapshot
            {
                _gen0 = GC.CollectionCount(0),
                _gen1 = GC.CollectionCount(1),
                _gen2 = GC.CollectionCount(2),
                _mem = GC.GetTotalMemory(false),
                _alloc = GC.GetAllocatedBytesForCurrentThread()
            };
        }

        public void LogIfGen2(string site)
        {
            int gen2 = GC.CollectionCount(2);
            int d2 = gen2 - _gen2;
            if (d2 > 0)
            {
                int tid = Environment.CurrentManagedThreadId;
                long allocNow = GC.GetAllocatedBytesForCurrentThread();
                long memNow = GC.GetTotalMemory(false);
                string st = Environment.StackTrace;
                int nl = st.IndexOf('\n');
                if (nl > 0) st = st.Substring(nl + 1);
                nl = st.IndexOf('\n');
                if (nl > 0) st = st.Substring(0, nl);
                Log.Info("GC-TRIGGER[TID={Tid}]: {Site} gen2+{D2} allocDelta={Alloc}KB heapDelta={Heap}KB call={Call}",
                    tid, site, d2, (allocNow - _alloc) / 1024, (memNow - _mem) / 1024, st.Replace("\r", ""));
            }
        }
    }
}
