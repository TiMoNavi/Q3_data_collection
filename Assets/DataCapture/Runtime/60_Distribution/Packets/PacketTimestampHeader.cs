namespace DataCapture.Networking
{
    [System.Serializable]
    public struct PacketTimestampHeader
    {
        public long frameId;
        public long timestampUnixMs;
        public long sequenceId;
        public string streamName;
    }
}
