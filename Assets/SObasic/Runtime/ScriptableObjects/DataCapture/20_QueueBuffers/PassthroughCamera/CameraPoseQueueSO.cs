using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "CameraPoseQueueSO", menuName = "DataCapture/10 Camera Capture/Camera Pose Queue")]
    public class CameraPoseQueueSO : ScriptableObject, IDataSource<CameraPoseRecord>, IRecordQueueSink, IQueueHealth
    {
        [SerializeField] private int capacity = 300;
        [SerializeField] private CameraPoseRecord[] debugSnapshot = new CameraPoseRecord[0];

        private RingBuffer<CameraPoseRecord> buffer;

        public int Count => Buffer.Count;
        public int Capacity => Buffer.Capacity;
        public long OldestTimestamp => Buffer.OldestTimestamp;
        public long NewestTimestamp => Buffer.NewestTimestamp;
        public long OverwriteCount => Buffer.OverwriteCount;
        public long LastClearTimestamp => Buffer.LastClearTimestamp;
        public long GenerationId => Buffer.GenerationId;
        public CameraPoseRecord[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(CameraPoseRecord);

        private RingBuffer<CameraPoseRecord> Buffer => buffer ??= new RingBuffer<CameraPoseRecord>(Mathf.Max(1, capacity));

        public void RecordData(CameraPoseRecord data) { Buffer.Add(data); RefreshDebugSnapshot(); }
        public CameraPoseRecord GetDataAt(long timestamp, long tolerance) => Buffer.GetNearest(timestamp, tolerance);
        public CameraPoseRecord[] GetDataInRange(long startTime, long endTime) => Buffer.GetInRange(startTime, endTime).ToArray();
        public CameraPoseRecord[] ExportSnapshot() { RefreshDebugSnapshot(); return debugSnapshot; }
        public void Clear() { Buffer.Clear(); RefreshDebugSnapshot(); }
        public void ClearQueue() => Clear();
        private void OnValidate() => capacity = Mathf.Max(1, capacity);
        private void RefreshDebugSnapshot() => debugSnapshot = Buffer.ToArray();

        public bool TryRecord(ITimestampedData record)
        {
            if (record is CameraPoseRecord typedRecord)
            {
                RecordData(typedRecord);
                return true;
            }

            return false;
        }
    }
}
