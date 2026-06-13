using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "CameraMetadataQueueSO", menuName = "DataCapture/10 Camera Capture/Camera Metadata Queue")]
    public class CameraMetadataQueueSO : ScriptableObject, IDataSource<CameraMetadataRecord>, IRecordQueueSink, IQueueHealth
    {
        [SerializeField] private int capacity = 300;
        [SerializeField] private CameraMetadataRecord[] debugSnapshot = new CameraMetadataRecord[0];

        private RingBuffer<CameraMetadataRecord> buffer;

        public int Count => Buffer.Count;
        public int Capacity => Buffer.Capacity;
        public long OldestTimestamp => Buffer.OldestTimestamp;
        public long NewestTimestamp => Buffer.NewestTimestamp;
        public long OverwriteCount => Buffer.OverwriteCount;
        public long LastClearTimestamp => Buffer.LastClearTimestamp;
        public long GenerationId => Buffer.GenerationId;
        public CameraMetadataRecord[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(CameraMetadataRecord);

        private RingBuffer<CameraMetadataRecord> Buffer => buffer ??= new RingBuffer<CameraMetadataRecord>(Mathf.Max(1, capacity));

        public void RecordData(CameraMetadataRecord data) { Buffer.Add(data); RefreshDebugSnapshot(); }
        public CameraMetadataRecord GetDataAt(long timestamp, long tolerance) => Buffer.GetNearest(timestamp, tolerance);
        public CameraMetadataRecord[] GetDataInRange(long startTime, long endTime) => Buffer.GetInRange(startTime, endTime).ToArray();
        public CameraMetadataRecord[] ExportSnapshot() { RefreshDebugSnapshot(); return debugSnapshot; }
        public void Clear() { Buffer.Clear(); RefreshDebugSnapshot(); }
        public void ClearQueue() => Clear();
        private void OnValidate() => capacity = Mathf.Max(1, capacity);
        private void RefreshDebugSnapshot() => debugSnapshot = Buffer.ToArray();

        public bool TryRecord(ITimestampedData record)
        {
            if (record is CameraMetadataRecord typedRecord)
            {
                RecordData(typedRecord);
                return true;
            }

            return false;
        }
    }
}
