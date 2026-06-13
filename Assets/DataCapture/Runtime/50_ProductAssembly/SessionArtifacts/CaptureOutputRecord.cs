using DataCapture.Synchronization;

namespace DataCapture.Networking
{
    public enum CaptureOutputKind
    {
        FramePacket,
        FileArtifact
    }

    public enum CaptureDeliveryKind
    {
        Stream,
        OneShot
    }

    public enum CapturePayloadKind
    {
        None,
        DebugJpeg,
        H264AccessUnit,
        H265AccessUnit,
        Mp4File,
        MetadataOnly
    }

    public enum CapturePayloadStorage
    {
        None,
        MemoryBytes,
        LocalFile,
        PendingFile
    }

    public enum CaptureMetadataMode
    {
        None,
        InlineSnapshot,
        SidecarFile
    }

    public enum CaptureOutputStatus
    {
        Pending,
        Ready,
        Dropped,
        Failed
    }

    public enum CaptureMetadataBindingStatus
    {
        None,
        ExactFrameId,
        TimestampFallback,
        MissingSnapshot,
        NotSendable,
        Timeout,
        Failed
    }

    [System.Serializable]
    public struct CapturePayloadRef
    {
        public CapturePayloadStorage storage;
        public byte[] memoryBytes;
        public string filePath;
        public string sessionId;
        public long byteLength;

        public static CapturePayloadRef FromBytes(byte[] bytes)
        {
            return new CapturePayloadRef
            {
                storage = bytes != null ? CapturePayloadStorage.MemoryBytes : CapturePayloadStorage.None,
                memoryBytes = bytes,
                filePath = string.Empty,
                sessionId = string.Empty,
                byteLength = bytes != null ? bytes.LongLength : 0
            };
        }

        public static CapturePayloadRef FromLocalFile(string path, long byteLength)
        {
            return new CapturePayloadRef
            {
                storage = string.IsNullOrWhiteSpace(path) ? CapturePayloadStorage.None : CapturePayloadStorage.LocalFile,
                memoryBytes = null,
                filePath = path ?? string.Empty,
                sessionId = string.Empty,
                byteLength = byteLength
            };
        }
    }

    [System.Serializable]
    public struct CaptureOutputRecord : ITimestampedData
    {
        public long outputId;
        public long timestampUnixMs;
        public CaptureOutputKind outputKind;
        public CaptureDeliveryKind deliveryKind;
        public CapturePayloadKind payloadKind;
        public CapturePayloadRef payloadRef;
        public CaptureMetadataMode metadataMode;
        public CaptureOutputStatus status;

        public long sourceCameraFrameId;
        public long sourceFrameStartId;
        public long sourceFrameEndId;
        public long timestampStartUnixMs;
        public long timestampEndUnixMs;

        public string codec;
        public int width;
        public int height;
        public int frameRate;
        public bool isKeyFrame;
        public int byteLength;

        public bool hasMergedSnapshot;
        public MergedFrameSnapshotRecord mergedSnapshot;
        public CaptureMetadataBindingStatus metadataBindingStatus;
        public long metadataTimestampDeltaMs;
        public string metadataSidecarPath;
        public string manifestPath;
        public string dropReason;

        public long Timestamp => timestampUnixMs;
    }
}
