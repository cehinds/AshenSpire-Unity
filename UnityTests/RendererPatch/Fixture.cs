// Executable model of the renderer's allocate/consolidate/alignment contract.
// The test patches this assembly and executes the resulting IL in an isolated context.
namespace UnityEngine.UIElements.UIR
{
    public sealed class DataSet
    {
        public struct Range { public uint start, count; }
        public readonly List<Range> dirtyRanges = new();
        public uint totalDirtyCount => (uint)dirtyRanges.Sum(range => (long)range.count);
        public void ConsolidateRanges(float density)
        {
            if (dirtyRanges.Count < 2) return;
            uint start = dirtyRanges[0].start, end = dirtyRanges[^1].start + dirtyRanges[^1].count;
            if (totalDirtyCount >= (end - start) * density)
            {
                dirtyRanges.Clear();
                dirtyRanges.Add(new Range { start = start, count = end - start });
            }
        }
    }
    public sealed class GpuUpdaterStaged
    {
        public readonly List<DataSet> Data = new();
        public int Requested, Copied;
        public void CompleteUpdate()
        {
            foreach (var data in Data)
            {
                var capacity = FindOrAllocateBuffer((int)data.totalDirtyCount);
                PrepareCopyRanges(data, capacity);
            }
        }
        private int FindOrAllocateBuffer(int count) { Requested = count; return count; }
        private void PrepareCopyRanges(DataSet data, int capacity)
        {
            data.ConsolidateRanges(.9f);
            var ranges = data.dirtyRanges;
            int count = ranges.Count;
            Copied = 0;
            foreach (var range in ranges)
                Copied += checked((int)(((range.start + range.count + 1) & ~1u) - (range.start & ~1u)));
        }
        public static int[] Run(uint[] starts, uint[] counts)
        {
            var data = new DataSet();
            for (int i = 0; i < starts.Length; i++) data.dirtyRanges.Add(new DataSet.Range { start = starts[i], count = counts[i] });
            var updater = new GpuUpdaterStaged(); updater.Data.Add(data); updater.CompleteUpdate();
            return new[] { updater.Requested, updater.Copied };
        }
    }
}
