namespace DataCapture.Synchronization
{
    [System.Serializable]
    public struct EncodedFrameRecord : ITimestampedData
    {
        public long encodedFrameId;
        public long sourceCameraFrameId;
        public long timestampUnixMs;
        public bool isKeyFrame;
        public int width;
        public int height;
        public string codec;
        public int byteLength;
        public string debugFilePath;

        public long Timestamp => timestampUnixMs;
    }
}
