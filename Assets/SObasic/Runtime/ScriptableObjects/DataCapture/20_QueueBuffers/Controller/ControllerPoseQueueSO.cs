using UnityEngine;
using SObasic.CurrentQueueBridge;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "ControllerPoseQueueSO", menuName = "DataCapture/30 Pose Metadata Capture/Controller/Controller Pose Queue")]
    public class ControllerPoseQueueSO : ScriptableObject, IDataSource<ControllerPoseRecord>, IRecordQueueSink, IQueueHealth
    {
        [SerializeField] private int capacity = 600;
        [SerializeField] private ControllerPoseRecord[] debugSnapshot = new ControllerPoseRecord[0];

        private RingBuffer<ControllerPoseRecord> buffer;

        public int Count => Buffer.Count;
        public int Capacity => Buffer.Capacity;
        public long OldestTimestamp => Buffer.OldestTimestamp;
        public long NewestTimestamp => Buffer.NewestTimestamp;
        public long OverwriteCount => Buffer.OverwriteCount;
        public long LastClearTimestamp => Buffer.LastClearTimestamp;
        public long GenerationId => Buffer.GenerationId;
        public ControllerPoseRecord[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(ControllerPoseRecord);

        private RingBuffer<ControllerPoseRecord> Buffer => buffer ??= new RingBuffer<ControllerPoseRecord>(Mathf.Max(1, capacity));

        public void RecordData(ControllerPoseRecord data)
        {
            Buffer.Add(data);
        }

        public ControllerPoseRecord GetDataAt(long timestamp, long tolerance)
        {
            return Buffer.GetNearest(timestamp, tolerance);
        }

        public ControllerPoseRecord[] GetDataInRange(long startTime, long endTime)
        {
            return Buffer.GetInRange(startTime, endTime).ToArray();
        }

        public ControllerPoseRecord[] ExportSnapshot()
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
            if (record is ControllerPoseRecord typedRecord)
            {
                RecordData(typedRecord);
                return true;
            }

            return false;
        }
    }
}
