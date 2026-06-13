using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Networking
{
    public enum EncodedAccessUnitKind
    {
        Unknown,
        CodecConfig,
        KeyFrame,
        DeltaFrame
    }

    [System.Serializable]
    public struct EncodedAccessUnitRecord : ITimestampedData
    {
        public long accessUnitId;
        public long frameId;
        public long sourceTimestampUnixMs;
        public long encodedPtsUs;
        public long timestampUnixMs;
        public string codec;
        public EncodedAccessUnitKind unitKind;
        public bool isKeyFrame;
        public int width;
        public int height;
        public byte[] sampleBytes;
        public string sampleFilePath;
        public int byteLength;

        public long Timestamp => timestampUnixMs != 0 ? timestampUnixMs : sourceTimestampUnixMs;
    }

    [CreateAssetMenu(fileName = "EncodedAccessUnitQueueSO", menuName = "DataCapture/40 Single Encode Production/Encoded Access Unit Queue")]
    public class EncodedAccessUnitQueueSO : ScriptableObject, IDataSource<EncodedAccessUnitRecord>, IRecordQueueSink, IQueueHealth
    {
        [SerializeField] private int capacity = 600;
        [SerializeField] private EncodedAccessUnitRecord[] debugSnapshot = new EncodedAccessUnitRecord[0];

        private RingBuffer<EncodedAccessUnitRecord> buffer;

        public int Count => Buffer.Count;
        public int Capacity => Buffer.Capacity;
        public long OldestTimestamp => Buffer.OldestTimestamp;
        public long NewestTimestamp => Buffer.NewestTimestamp;
        public long OverwriteCount => Buffer.OverwriteCount;
        public long LastClearTimestamp => Buffer.LastClearTimestamp;
        public long GenerationId => Buffer.GenerationId;
        public EncodedAccessUnitRecord[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(EncodedAccessUnitRecord);

        private RingBuffer<EncodedAccessUnitRecord> Buffer => buffer ??= new RingBuffer<EncodedAccessUnitRecord>(Mathf.Max(1, capacity));

        public void RecordData(EncodedAccessUnitRecord data)
        {
            Buffer.Add(data);
            RefreshDebugSnapshot();
        }

        public EncodedAccessUnitRecord GetDataAt(long timestamp, long tolerance)
        {
            return Buffer.GetNearest(timestamp, tolerance);
        }

        public EncodedAccessUnitRecord[] GetDataInRange(long startTime, long endTime)
        {
            return Buffer.GetInRange(startTime, endTime).ToArray();
        }

        public EncodedAccessUnitRecord[] ExportSnapshot()
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
            if (record is EncodedAccessUnitRecord typedRecord)
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
