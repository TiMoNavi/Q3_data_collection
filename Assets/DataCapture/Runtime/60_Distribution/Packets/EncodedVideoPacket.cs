using DataCapture.Synchronization;

namespace DataCapture.Networking
{
    [System.Serializable]
    public class EncodedVideoPacket
    {
        public PacketTimestampHeader header;
        public long encodedFrameId;
        public long sourceCameraFrameId;
        public bool isKeyFrame;
        public string codec;
        public int width;
        public int height;
        public byte[] payload;

        public static EncodedVideoPacket FromRecord(EncodedFrameRecord record, byte[] payload, long sequenceId)
        {
            return new EncodedVideoPacket
            {
                header = new PacketTimestampHeader
                {
                    frameId = record.sourceCameraFrameId,
                    timestampUnixMs = record.timestampUnixMs,
                    sequenceId = sequenceId,
                    streamName = "video"
                },
                encodedFrameId = record.encodedFrameId,
                sourceCameraFrameId = record.sourceCameraFrameId,
                isKeyFrame = record.isKeyFrame,
                codec = record.codec,
                width = record.width,
                height = record.height,
                payload = payload ?? new byte[0]
            };
        }
    }

    [System.Serializable]
    public class EncodedVideoPacketHeader
    {
        public PacketTimestampHeader header;
        public long encodedFrameId;
        public long sourceCameraFrameId;
        public bool isKeyFrame;
        public string codec;
        public int width;
        public int height;
        public int payloadByteLength;
        public string contentType;

        public static EncodedVideoPacketHeader FromRecord(EncodedFrameRecord record, long sequenceId)
        {
            return new EncodedVideoPacketHeader
            {
                header = new PacketTimestampHeader
                {
                    frameId = record.sourceCameraFrameId,
                    timestampUnixMs = record.timestampUnixMs,
                    sequenceId = sequenceId,
                    streamName = "video"
                },
                encodedFrameId = record.encodedFrameId,
                sourceCameraFrameId = record.sourceCameraFrameId,
                isKeyFrame = record.isKeyFrame,
                codec = record.codec,
                width = record.width,
                height = record.height,
                payloadByteLength = record.byteLength,
                contentType = ResolveContentType(record.codec)
            };
        }

        private static string ResolveContentType(string codec)
        {
            if (codec == "DEBUG_JPEG")
            {
                return "image/jpeg";
            }

            if (codec == "DEBUG_MJPEG" || codec == "MJPEG")
            {
                return "video/x-motion-jpeg";
            }

            if (codec == "H264")
            {
                return "video/h264";
            }

            if (codec == "H265" || codec == "HEVC")
            {
                return "video/h265";
            }

            return "application/octet-stream";
        }
    }
}
