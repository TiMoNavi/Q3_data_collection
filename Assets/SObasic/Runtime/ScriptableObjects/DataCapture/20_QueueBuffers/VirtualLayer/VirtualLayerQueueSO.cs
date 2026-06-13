using UnityEngine;
using SObasic.CurrentQueueBridge;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "VirtualLayerQueueSO", menuName = "DataCapture/20 Virtual Layer Capture/Virtual Layer Queue")]
    public class VirtualLayerQueueSO : ScriptableObject, IDataSource<VirtualLayerFrameRecord>, IRecordQueueSink, IQueueHealth
    {
        [SerializeField] private int capacity = 300;
        [SerializeField] private VirtualLayerFrameRecord[] debugSnapshot = new VirtualLayerFrameRecord[0];

        private RingBuffer<VirtualLayerFrameRecord> buffer;

        public int Count => Buffer.Count;
        public int Capacity => Buffer.Capacity;
        public long OldestTimestamp => Buffer.OldestTimestamp;
        public long NewestTimestamp => Buffer.NewestTimestamp;
        public long OverwriteCount => Buffer.OverwriteCount;
        public long LastClearTimestamp => Buffer.LastClearTimestamp;
        public long GenerationId => Buffer.GenerationId;
        public VirtualLayerFrameRecord[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(VirtualLayerFrameRecord);

        private RingBuffer<VirtualLayerFrameRecord> Buffer => buffer ??= new RingBuffer<VirtualLayerFrameRecord>(Mathf.Max(1, capacity));

        public void RecordData(VirtualLayerFrameRecord data)
        {
            Buffer.Add(data);
            RefreshDebugSnapshot();
        }

        public VirtualLayerFrameRecord GetDataAt(long timestamp, long tolerance)
        {
            return Buffer.GetNearest(timestamp, tolerance);
        }

        public VirtualLayerFrameRecord[] GetDataInRange(long startTime, long endTime)
        {
            return Buffer.GetInRange(startTime, endTime).ToArray();
        }

        public VirtualLayerFrameRecord[] ExportSnapshot()
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
            if (record is VirtualLayerFrameRecord typedRecord)
            {
                RecordData(typedRecord);
                return true;
            }

            return false;
        }
    }
}
