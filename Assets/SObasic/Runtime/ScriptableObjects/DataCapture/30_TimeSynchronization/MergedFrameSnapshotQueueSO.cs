using UnityEngine;
using SObasic.CurrentQueueBridge;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "MergedFrameSnapshotQueueSO", menuName = "DataCapture/30 Time Synchronization/Merged Frame Snapshot Queue")]
    public class MergedFrameSnapshotQueueSO : ScriptableObject, IDataSource<MergedFrameSnapshotRecord>, IRecordQueueSink, IQueueHealth
    {
        [SerializeField] private int capacity = 300;
        [SerializeField] private MergedFrameSnapshotRecord[] debugSnapshot = new MergedFrameSnapshotRecord[0];

        private RingBuffer<MergedFrameSnapshotRecord> buffer;

        public int Count => Buffer.Count;
        public int Capacity => Buffer.Capacity;
        public long OldestTimestamp => Buffer.OldestTimestamp;
        public long NewestTimestamp => Buffer.NewestTimestamp;
        public long OverwriteCount => Buffer.OverwriteCount;
        public long LastClearTimestamp => Buffer.LastClearTimestamp;
        public long GenerationId => Buffer.GenerationId;
        public MergedFrameSnapshotRecord[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(MergedFrameSnapshotRecord);

        private RingBuffer<MergedFrameSnapshotRecord> Buffer => buffer ??= new RingBuffer<MergedFrameSnapshotRecord>(Mathf.Max(1, capacity));

        public void RecordData(MergedFrameSnapshotRecord data)
        {
            Buffer.Add(data);
            RefreshDebugSnapshot();
        }

        public MergedFrameSnapshotRecord GetDataAt(long timestamp, long tolerance)
        {
            return Buffer.GetNearest(timestamp, tolerance);
        }

        public MergedFrameSnapshotRecord[] GetDataInRange(long startTime, long endTime)
        {
            return Buffer.GetInRange(startTime, endTime).ToArray();
        }

        public MergedFrameSnapshotRecord[] ExportSnapshot()
        {
            RefreshDebugSnapshot();
            return debugSnapshot;
        }

        public bool TryGetLatest(out MergedFrameSnapshotRecord record)
        {
            MergedFrameSnapshotRecord[] records = ExportSnapshot();
            if (records.Length == 0)
            {
                record = default;
                return false;
            }

            record = records[records.Length - 1];
            return true;
        }

        public bool TryGetLatestSendable(out MergedFrameSnapshotRecord record)
        {
            MergedFrameSnapshotRecord[] records = ExportSnapshot();
            for (int i = records.Length - 1; i >= 0; i--)
            {
                if (records[i].isSendable)
                {
                    record = records[i];
                    return true;
                }
            }

            record = default;
            return false;
        }

        public MergedFrameSnapshotRecord[] ExportSendableSnapshot()
        {
            MergedFrameSnapshotRecord[] records = ExportSnapshot();
            int count = 0;
            for (int i = 0; i < records.Length; i++)
            {
                if (records[i].isSendable)
                {
                    count++;
                }
            }

            var sendable = new MergedFrameSnapshotRecord[count];
            int index = 0;
            for (int i = 0; i < records.Length; i++)
            {
                if (records[i].isSendable)
                {
                    sendable[index++] = records[i];
                }
            }

            return sendable;
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

        private void OnValidate()
        {
            capacity = Mathf.Max(1, capacity);
        }

        private void RefreshDebugSnapshot()
        {
            debugSnapshot = Buffer.ToArray();
        }

        public bool TryRecord(ITimestampedData record)
        {
            if (record is MergedFrameSnapshotRecord typedRecord)
            {
                RecordData(typedRecord);
                return true;
            }

            return false;
        }
    }
}
