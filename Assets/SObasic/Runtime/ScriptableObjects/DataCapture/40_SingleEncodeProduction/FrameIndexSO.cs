using UnityEngine;

namespace DataCapture.Networking
{
    [System.Serializable]
    public struct FrameIndexEntry
    {
        public long frameId;
        public long sourceTimestampUnixMs;
        public long accessUnitId;
        public long encodedPtsUs;
        public int mp4SampleIndex;
        public long metadataTimelineEntryId;
    }

    [CreateAssetMenu(fileName = "FrameIndexSO", menuName = "DataCapture/40 Single Encode Production/Frame Index")]
    public class FrameIndexSO : ScriptableObject
    {
        [SerializeField] private int capacity = 1800;
        [SerializeField] private FrameIndexEntry[] entries = new FrameIndexEntry[0];

        public FrameIndexEntry[] Entries => entries;
        public int Count => entries != null ? entries.Length : 0;

        public void SetEntries(FrameIndexEntry[] nextEntries)
        {
            entries = nextEntries ?? new FrameIndexEntry[0];
            TrimToCapacity();
        }

        public void Append(FrameIndexEntry entry)
        {
            var oldEntries = entries ?? new FrameIndexEntry[0];
            var next = new FrameIndexEntry[oldEntries.Length + 1];
            oldEntries.CopyTo(next, 0);
            next[next.Length - 1] = entry;
            entries = next;
            TrimToCapacity();
        }

        public void Clear()
        {
            entries = new FrameIndexEntry[0];
        }

        private void OnValidate()
        {
            capacity = Mathf.Max(1, capacity);
            TrimToCapacity();
        }

        private void TrimToCapacity()
        {
            if (entries == null || entries.Length <= capacity)
            {
                return;
            }

            var trimmed = new FrameIndexEntry[capacity];
            System.Array.Copy(entries, entries.Length - capacity, trimmed, 0, capacity);
            entries = trimmed;
        }
    }
}
