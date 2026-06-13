using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "CameraImageQueueSO", menuName = "DataCapture/10 Camera Capture/Camera Image Queue")]
    public class CameraImageQueueSO : ScriptableObject, IDataSource<CameraImageFrameRecord>, IRecordQueueSink, IQueueHealth
    {
        [SerializeField] private int capacity = 300;
        [SerializeField] private CameraImageFrameRecord[] debugSnapshot = new CameraImageFrameRecord[0];

        private RingBuffer<CameraImageFrameRecord> buffer;

        public int Count => Buffer.Count;
        public int Capacity => Buffer.Capacity;
        public long OldestTimestamp => Buffer.OldestTimestamp;
        public long NewestTimestamp => Buffer.NewestTimestamp;
        public long OverwriteCount => Buffer.OverwriteCount;
        public long LastClearTimestamp => Buffer.LastClearTimestamp;
        public long GenerationId => Buffer.GenerationId;
        public CameraImageFrameRecord[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(CameraImageFrameRecord);

        private RingBuffer<CameraImageFrameRecord> Buffer => buffer ??= new RingBuffer<CameraImageFrameRecord>(Mathf.Max(1, capacity));

        public void RecordData(CameraImageFrameRecord data)
        {
            Buffer.Add(data);
            RefreshDebugSnapshot();
        }

        public CameraImageFrameRecord GetDataAt(long timestamp, long tolerance) => Buffer.GetNearest(timestamp, tolerance);
        public CameraImageFrameRecord[] GetDataInRange(long startTime, long endTime) => Buffer.GetInRange(startTime, endTime).ToArray();
        public CameraImageFrameRecord[] ExportSnapshot() { RefreshDebugSnapshot(); return debugSnapshot; }
        public void Clear() { Buffer.Clear(); RefreshDebugSnapshot(); }
        public void ClearQueue() => Clear();
        private void OnValidate() => capacity = Mathf.Max(1, capacity);
        private void RefreshDebugSnapshot() => debugSnapshot = Buffer.ToArray();

        public bool TryRecord(ITimestampedData record)
        {
            if (record is CameraImageFrameRecord typedRecord)
            {
                RecordData(typedRecord);
                return true;
            }

            return false;
        }
    }
}
