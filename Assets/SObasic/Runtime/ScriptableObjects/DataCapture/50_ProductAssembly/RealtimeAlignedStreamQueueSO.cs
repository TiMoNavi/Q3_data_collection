using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Networking
{
    [System.Serializable]
    public struct RealtimeAlignedStreamRecord : ITimestampedData
    {
        public long recordId;
        public long frameId;
        public long timestampUnixMs;
        public long accessUnitId;
        public long metadataTimelineEntryId;
        public EncodedAccessUnitRecord accessUnit;
        public MetadataTimelineEntryRecord metadataEntry;
        public string dropReason;

        public long Timestamp => timestampUnixMs;
    }

    [CreateAssetMenu(fileName = "RealtimeAlignedStreamQueueSO", menuName = "DataCapture/50 Product Assembly/Realtime Aligned Stream Queue")]
    public class RealtimeAlignedStreamQueueSO : ScriptableObject, IDataSource<RealtimeAlignedStreamRecord>, IRecordQueueSink, IQueueHealth
    {
        [SerializeField] private int capacity = 600;
        [SerializeField] private RealtimeAlignedStreamRecord[] debugSnapshot = new RealtimeAlignedStreamRecord[0];

        private RingBuffer<RealtimeAlignedStreamRecord> buffer;

        public int Count => Buffer.Count;
        public int Capacity => Buffer.Capacity;
        public long OldestTimestamp => Buffer.OldestTimestamp;
        public long NewestTimestamp => Buffer.NewestTimestamp;
        public long OverwriteCount => Buffer.OverwriteCount;
        public long LastClearTimestamp => Buffer.LastClearTimestamp;
        public long GenerationId => Buffer.GenerationId;
        public RealtimeAlignedStreamRecord[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(RealtimeAlignedStreamRecord);

        private RingBuffer<RealtimeAlignedStreamRecord> Buffer => buffer ??= new RingBuffer<RealtimeAlignedStreamRecord>(Mathf.Max(1, capacity));

        public void RecordData(RealtimeAlignedStreamRecord data)
        {
            Buffer.Add(data);
            RefreshDebugSnapshot();
        }

        public RealtimeAlignedStreamRecord GetDataAt(long timestamp, long tolerance)
        {
            return Buffer.GetNearest(timestamp, tolerance);
        }

        public RealtimeAlignedStreamRecord[] GetDataInRange(long startTime, long endTime)
        {
            return Buffer.GetInRange(startTime, endTime).ToArray();
        }

        public RealtimeAlignedStreamRecord[] ExportSnapshot()
        {
            RefreshDebugSnapshot();
            return debugSnapshot;
        }

        public void Clear()
        {
            Buffer.Clear();
            RefreshDebugSnapshot();
        }

        public void ClearQueue()
        {
            Clear();
        }

        public bool TryRecord(ITimestampedData record)
        {
            if (record is RealtimeAlignedStreamRecord typedRecord)
            {
                RecordData(typedRecord);
                return true;
            }

            return false;
        }

        private void OnValidate()
        {
            capacity = Mathf.Max(1, capacity);
        }

        private void RefreshDebugSnapshot()
        {
            debugSnapshot = Buffer.ToArray();
        }
    }
}
