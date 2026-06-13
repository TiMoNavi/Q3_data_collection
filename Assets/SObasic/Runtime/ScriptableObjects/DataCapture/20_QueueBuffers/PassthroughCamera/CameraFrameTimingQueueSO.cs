using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "CameraFrameTimingQueueSO", menuName = "DataCapture/10 Camera Capture/Camera Frame Timing Queue")]
    public class CameraFrameTimingQueueSO : ScriptableObject, IDataSource<CameraFrameTimingRecord>, IRecordQueueSink, IQueueHealth
    {
        [SerializeField] private int capacity = 300;
        [SerializeField] private CameraFrameTimingRecord[] debugSnapshot = new CameraFrameTimingRecord[0];

        private RingBuffer<CameraFrameTimingRecord> buffer;

        public int Count => Buffer.Count;
        public int Capacity => Buffer.Capacity;
        public long OldestTimestamp => Buffer.OldestTimestamp;
        public long NewestTimestamp => Buffer.NewestTimestamp;
        public long OverwriteCount => Buffer.OverwriteCount;
        public long LastClearTimestamp => Buffer.LastClearTimestamp;
        public long GenerationId => Buffer.GenerationId;
        public CameraFrameTimingRecord[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(CameraFrameTimingRecord);

        private RingBuffer<CameraFrameTimingRecord> Buffer => buffer ??= new RingBuffer<CameraFrameTimingRecord>(Mathf.Max(1, capacity));

        public void RecordData(CameraFrameTimingRecord data) { Buffer.Add(data); RefreshDebugSnapshot(); }
        public CameraFrameTimingRecord GetDataAt(long timestamp, long tolerance) => Buffer.GetNearest(timestamp, tolerance);
        public CameraFrameTimingRecord[] GetDataInRange(long startTime, long endTime) => Buffer.GetInRange(startTime, endTime).ToArray();
        public CameraFrameTimingRecord[] ExportSnapshot() { RefreshDebugSnapshot(); return debugSnapshot; }
        public void Clear() { Buffer.Clear(); RefreshDebugSnapshot(); }
        public void ClearQueue() => Clear();
        private void OnValidate() => capacity = Mathf.Max(1, capacity);
        private void RefreshDebugSnapshot() => debugSnapshot = Buffer.ToArray();

        public bool TryRecord(ITimestampedData record)
        {
            if (record is CameraFrameTimingRecord typedRecord)
            {
                RecordData(typedRecord);
                return true;
            }

            return false;
        }
    }
}
