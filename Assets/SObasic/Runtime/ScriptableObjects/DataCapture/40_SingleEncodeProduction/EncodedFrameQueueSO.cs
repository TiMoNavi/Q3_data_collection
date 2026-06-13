using UnityEngine;
using SObasic.CurrentQueueBridge;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "EncodedFrameQueueSO", menuName = "DataCapture/50 Encoding Network/Encoded Frame Queue")]
    public class EncodedFrameQueueSO : ScriptableObject, IDataSource<EncodedFrameRecord>, IRecordQueueSink
    {
        [SerializeField] private int capacity = 300;
        [SerializeField] private EncodedFrameRecord[] debugSnapshot = new EncodedFrameRecord[0];

        private RingBuffer<EncodedFrameRecord> buffer;

        public int Count => Buffer.Count;
        public int Capacity => Buffer.Capacity;
        public EncodedFrameRecord[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(EncodedFrameRecord);

        private RingBuffer<EncodedFrameRecord> Buffer => buffer ??= new RingBuffer<EncodedFrameRecord>(Mathf.Max(1, capacity));

        public void RecordData(EncodedFrameRecord data)
        {
            Buffer.Add(data);
            RefreshDebugSnapshot();
        }

        public EncodedFrameRecord GetDataAt(long timestamp, long tolerance)
        {
            return Buffer.GetNearest(timestamp, tolerance);
        }

        public EncodedFrameRecord[] GetDataInRange(long startTime, long endTime)
        {
            return Buffer.GetInRange(startTime, endTime).ToArray();
        }

        public EncodedFrameRecord[] ExportSnapshot()
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
            if (record is EncodedFrameRecord typedRecord)
            {
                RecordData(typedRecord);
                return true;
            }

            return false;
        }
    }
}
