using UnityEngine;
using SObasic.CurrentQueueBridge;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "HeadsetPoseQueueSO", menuName = "DataCapture/30 Pose Metadata Capture/Headset/Headset Pose Queue")]
    public class HeadsetPoseQueueSO : ScriptableObject, IDataSource<HeadsetPoseRecord>, IRecordQueueSink, IQueueHealth
    {
        [SerializeField] private int capacity = 600;
        [SerializeField] private HeadsetPoseRecord[] debugSnapshot = new HeadsetPoseRecord[0];

        private RingBuffer<HeadsetPoseRecord> buffer;

        public int Count => Buffer.Count;
        public int Capacity => Buffer.Capacity;
        public long OldestTimestamp => Buffer.OldestTimestamp;
        public long NewestTimestamp => Buffer.NewestTimestamp;
        public long OverwriteCount => Buffer.OverwriteCount;
        public long LastClearTimestamp => Buffer.LastClearTimestamp;
        public long GenerationId => Buffer.GenerationId;
        public HeadsetPoseRecord[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(HeadsetPoseRecord);

        private RingBuffer<HeadsetPoseRecord> Buffer => buffer ??= new RingBuffer<HeadsetPoseRecord>(Mathf.Max(1, capacity));

        public void RecordData(HeadsetPoseRecord data)
        {
            Buffer.Add(data);
        }

        public HeadsetPoseRecord GetDataAt(long timestamp, long tolerance)
        {
            return Buffer.GetNearest(timestamp, tolerance);
        }

        public HeadsetPoseRecord[] GetDataInRange(long startTime, long endTime)
        {
            return Buffer.GetInRange(startTime, endTime).ToArray();
        }

        public HeadsetPoseRecord[] ExportSnapshot()
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
            if (record is HeadsetPoseRecord typedRecord)
            {
                RecordData(typedRecord);
                return true;
            }

            return false;
        }
    }
}
