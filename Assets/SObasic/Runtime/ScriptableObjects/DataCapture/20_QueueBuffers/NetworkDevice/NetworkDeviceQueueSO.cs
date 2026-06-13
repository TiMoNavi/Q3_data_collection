using UnityEngine;
using SObasic.CurrentQueueBridge;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "NetworkDeviceQueueSO", menuName = "DataCapture/30 Pose Metadata Capture/Network Device/Network Device Queue")]
    public class NetworkDeviceQueueSO : ScriptableObject, IDataSource<NetworkDeviceRecord>, IRecordQueueSink, IQueueHealth
    {
        [SerializeField] private int capacity = 300;
        [SerializeField] private NetworkDeviceRecord[] debugSnapshot = new NetworkDeviceRecord[0];

        private RingBuffer<NetworkDeviceRecord> buffer;

        public int Count => Buffer.Count;
        public int Capacity => Buffer.Capacity;
        public long OldestTimestamp => Buffer.OldestTimestamp;
        public long NewestTimestamp => Buffer.NewestTimestamp;
        public long OverwriteCount => Buffer.OverwriteCount;
        public long LastClearTimestamp => Buffer.LastClearTimestamp;
        public long GenerationId => Buffer.GenerationId;
        public NetworkDeviceRecord[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(NetworkDeviceRecord);

        private RingBuffer<NetworkDeviceRecord> Buffer => buffer ??= new RingBuffer<NetworkDeviceRecord>(Mathf.Max(1, capacity));

        public void RecordData(NetworkDeviceRecord data)
        {
            Buffer.Add(data);
            RefreshDebugSnapshot();
        }

        public NetworkDeviceRecord GetDataAt(long timestamp, long tolerance)
        {
            return Buffer.GetNearest(timestamp, tolerance);
        }

        public NetworkDeviceRecord[] GetDataInRange(long startTime, long endTime)
        {
            return Buffer.GetInRange(startTime, endTime).ToArray();
        }

        public NetworkDeviceRecord[] ExportSnapshot()
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
            if (record is NetworkDeviceRecord typedRecord)
            {
                RecordData(typedRecord);
                return true;
            }

            return false;
        }
    }
}
