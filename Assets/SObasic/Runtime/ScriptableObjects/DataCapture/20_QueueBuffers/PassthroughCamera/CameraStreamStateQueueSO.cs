using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "CameraStreamStateQueueSO", menuName = "DataCapture/10 Camera Capture/Camera Stream State Queue")]
    public class CameraStreamStateQueueSO : ScriptableObject, IDataSource<CameraStreamStateRecord>, IRecordQueueSink, IQueueHealth
    {
        [SerializeField] private int capacity = 300;
        [SerializeField] private CameraStreamStateRecord[] debugSnapshot = new CameraStreamStateRecord[0];

        private RingBuffer<CameraStreamStateRecord> buffer;

        public int Count => Buffer.Count;
        public int Capacity => Buffer.Capacity;
        public long OldestTimestamp => Buffer.OldestTimestamp;
        public long NewestTimestamp => Buffer.NewestTimestamp;
        public long OverwriteCount => Buffer.OverwriteCount;
        public long LastClearTimestamp => Buffer.LastClearTimestamp;
        public long GenerationId => Buffer.GenerationId;
        public CameraStreamStateRecord[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(CameraStreamStateRecord);

        private RingBuffer<CameraStreamStateRecord> Buffer => buffer ??= new RingBuffer<CameraStreamStateRecord>(Mathf.Max(1, capacity));

        public void RecordData(CameraStreamStateRecord data) { Buffer.Add(data); RefreshDebugSnapshot(); }
        public CameraStreamStateRecord GetDataAt(long timestamp, long tolerance) => Buffer.GetNearest(timestamp, tolerance);
        public CameraStreamStateRecord[] GetDataInRange(long startTime, long endTime) => Buffer.GetInRange(startTime, endTime).ToArray();
        public CameraStreamStateRecord[] ExportSnapshot() { RefreshDebugSnapshot(); return debugSnapshot; }
        public void Clear() { Buffer.Clear(); RefreshDebugSnapshot(); }
        public void ClearQueue() => Clear();
        private void OnValidate() => capacity = Mathf.Max(1, capacity);
        private void RefreshDebugSnapshot() => debugSnapshot = Buffer.ToArray();

        public bool TryRecord(ITimestampedData record)
        {
            if (record is CameraStreamStateRecord typedRecord)
            {
                RecordData(typedRecord);
                return true;
            }

            return false;
        }
    }
}
