using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Synchronization
{
    [System.Serializable]
    public struct MetadataTimelineEntryRecord : ITimestampedData
    {
        public long entryId;
        public long frameId;
        public long timestampUnixMs;
        public long sourceTimestampUnixMs;
        public bool isSendable;
        public string metadataJson;
        public string dropReason;

        public long Timestamp => timestampUnixMs;
    }

    [CreateAssetMenu(fileName = "MetadataTimelineJournalSO", menuName = "DataCapture/30 Time Synchronization/Metadata Timeline Journal")]
    public class MetadataTimelineJournalSO : ScriptableObject, IDataSource<MetadataTimelineEntryRecord>, IRecordQueueSink, IQueueHealth
    {
        [SerializeField] private int capacity = 1800;
        [SerializeField] private MetadataTimelineEntryRecord[] debugSnapshot = new MetadataTimelineEntryRecord[0];

        private RingBuffer<MetadataTimelineEntryRecord> buffer;

        public int Count => Buffer.Count;
        public int Capacity => Buffer.Capacity;
        public long OldestTimestamp => Buffer.OldestTimestamp;
        public long NewestTimestamp => Buffer.NewestTimestamp;
        public long OverwriteCount => Buffer.OverwriteCount;
        public long LastClearTimestamp => Buffer.LastClearTimestamp;
        public long GenerationId => Buffer.GenerationId;
        public MetadataTimelineEntryRecord[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(MetadataTimelineEntryRecord);

        private RingBuffer<MetadataTimelineEntryRecord> Buffer => buffer ??= new RingBuffer<MetadataTimelineEntryRecord>(Mathf.Max(1, capacity));

        public void RecordData(MetadataTimelineEntryRecord data)
        {
            Buffer.Add(data);
            RefreshDebugSnapshot();
        }

        public MetadataTimelineEntryRecord GetDataAt(long timestamp, long tolerance)
        {
            return Buffer.GetNearest(timestamp, tolerance);
        }

        public MetadataTimelineEntryRecord[] GetDataInRange(long startTime, long endTime)
        {
            return Buffer.GetInRange(startTime, endTime).ToArray();
        }

        public MetadataTimelineEntryRecord[] ExportSnapshot()
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
            if (record is MetadataTimelineEntryRecord typedRecord)
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
