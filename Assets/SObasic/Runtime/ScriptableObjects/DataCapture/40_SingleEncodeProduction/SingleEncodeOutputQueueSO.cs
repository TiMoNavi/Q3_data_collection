using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Networking
{
    public enum SingleEncodeOutputStatus
    {
        Pending,
        Ready,
        Failed
    }

    public enum SingleEncodeVideoArtifactKind
    {
        None,
        EncodedAccessUnitSequence,
        Mp4File
    }

    [System.Serializable]
    public struct SingleEncodeTimestampSample
    {
        public long frameId;
        public long sourceTimestampUnixMs;
        public long encodedPtsUs;
        public long accessUnitId;
        public int mp4SampleIndex;
        public long metadataTimelineEntryId;
    }

    [System.Serializable]
    public struct SingleEncodeOutputRecord : ITimestampedData
    {
        public long outputId;
        public SingleEncodeOutputStatus status;
        public SingleEncodeVideoArtifactKind artifactKind;
        public string sessionId;
        public string codec;
        public int width;
        public int height;
        public int frameRate;
        public string videoArtifactPath;
        public string metadataTimelinePath;
        public string frameIndexPath;
        public long byteLength;
        public long startedUnixMs;
        public long finalizedUnixMs;
        public long firstFrameId;
        public long lastFrameId;
        public long timestampStartUnixMs;
        public long timestampEndUnixMs;
        public int frameCount;
        public int accessUnitCount;
        public int timestampSampleCount;
        public int metadataTimelineEntryCount;
        public int frameIndexEntryCount;
        public bool videoArtifactComplete;
        public SingleEncodeTimestampSample[] timestampSamples;
        public MetadataTimelineEntryRecord[] metadataTimelineEntries;
        public FrameIndexEntry[] frameIndexEntries;
        public string failureReason;

        public long Timestamp => timestampEndUnixMs > 0 ? timestampEndUnixMs : finalizedUnixMs;
        public bool IsReady => status == SingleEncodeOutputStatus.Ready && string.IsNullOrWhiteSpace(failureReason);
        public bool HasCompleteMetadataTimeline => metadataTimelineEntryCount > 0 &&
            metadataTimelineEntries != null &&
            metadataTimelineEntries.Length >= metadataTimelineEntryCount;
        public bool HasCompleteFrameIndex => frameIndexEntryCount > 0 &&
            frameIndexEntries != null &&
            frameIndexEntries.Length >= frameIndexEntryCount;
        public bool HasCompleteVideoArtifact => videoArtifactComplete ||
            (artifactKind != SingleEncodeVideoArtifactKind.None &&
                (accessUnitCount > 0 || !string.IsNullOrWhiteSpace(videoArtifactPath)));
    }

    [CreateAssetMenu(fileName = "SingleEncodeOutputQueueSO", menuName = "DataCapture/40 Single Encode Production/Single Encode Output Queue")]
    public class SingleEncodeOutputQueueSO : ScriptableObject, IDataSource<SingleEncodeOutputRecord>, IRecordQueueSink, IQueueHealth
    {
        [SerializeField] private int capacity = 32;
        [SerializeField] private SingleEncodeOutputRecord[] debugSnapshot = new SingleEncodeOutputRecord[0];

        private RingBuffer<SingleEncodeOutputRecord> buffer;

        public int Count => Buffer.Count;
        public int Capacity => Buffer.Capacity;
        public long OldestTimestamp => Buffer.OldestTimestamp;
        public long NewestTimestamp => Buffer.NewestTimestamp;
        public long OverwriteCount => Buffer.OverwriteCount;
        public long LastClearTimestamp => Buffer.LastClearTimestamp;
        public long GenerationId => Buffer.GenerationId;
        public SingleEncodeOutputRecord[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(SingleEncodeOutputRecord);

        private RingBuffer<SingleEncodeOutputRecord> Buffer => buffer ??= new RingBuffer<SingleEncodeOutputRecord>(Mathf.Max(1, capacity));

        public void RecordData(SingleEncodeOutputRecord data)
        {
            Buffer.Add(data);
            RefreshDebugSnapshot();
        }

        public SingleEncodeOutputRecord GetDataAt(long timestamp, long tolerance)
        {
            return Buffer.GetNearest(timestamp, tolerance);
        }

        public SingleEncodeOutputRecord[] GetDataInRange(long startTime, long endTime)
        {
            return Buffer.GetInRange(startTime, endTime).ToArray();
        }

        public SingleEncodeOutputRecord[] ExportSnapshot()
        {
            RefreshDebugSnapshot();
            return debugSnapshot;
        }

        public bool TryGetLatestReady(out SingleEncodeOutputRecord record)
        {
            SingleEncodeOutputRecord[] records = ExportSnapshot();
            for (int i = records.Length - 1; i >= 0; i--)
            {
                if (records[i].IsReady)
                {
                    record = records[i];
                    return true;
                }
            }

            record = default;
            return false;
        }

        public bool TryGetLatest(out SingleEncodeOutputRecord record)
        {
            SingleEncodeOutputRecord[] records = ExportSnapshot();
            if (records.Length == 0)
            {
                record = default;
                return false;
            }

            record = records[records.Length - 1];
            return true;
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
            if (record is SingleEncodeOutputRecord typedRecord)
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
