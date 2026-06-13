using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "CaptureOutputQueueSO", menuName = "DataCapture/50 Encoding Network/Capture Output Queue")]
    public class CaptureOutputQueueSO : ScriptableObject, IDataSource<CaptureOutputRecord>, IRecordQueueSink, IQueueHealth
    {
        [SerializeField] private int capacity = 300;
        [SerializeField] private CaptureOutputRecord[] debugSnapshot = new CaptureOutputRecord[0];

        private RingBuffer<CaptureOutputRecord> buffer;

        public int Count => Buffer.Count;
        public int Capacity => Buffer.Capacity;
        public long OldestTimestamp => Buffer.OldestTimestamp;
        public long NewestTimestamp => Buffer.NewestTimestamp;
        public long OverwriteCount => Buffer.OverwriteCount;
        public long LastClearTimestamp => Buffer.LastClearTimestamp;
        public long GenerationId => Buffer.GenerationId;
        public CaptureOutputRecord[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(CaptureOutputRecord);

        private RingBuffer<CaptureOutputRecord> Buffer => buffer ??= new RingBuffer<CaptureOutputRecord>(Mathf.Max(1, capacity));

        public void RecordData(CaptureOutputRecord data)
        {
            Buffer.Add(data);
            RefreshDebugSnapshot();
        }

        public CaptureOutputRecord GetDataAt(long timestamp, long tolerance)
        {
            return Buffer.GetNearest(timestamp, tolerance);
        }

        public CaptureOutputRecord[] GetDataInRange(long startTime, long endTime)
        {
            return Buffer.GetInRange(startTime, endTime).ToArray();
        }

        public CaptureOutputRecord[] ExportSnapshot()
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
            if (record is CaptureOutputRecord typedRecord)
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
