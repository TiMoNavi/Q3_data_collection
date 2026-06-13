namespace DataCapture.Synchronization
{
    [System.Serializable]
    public struct NetworkDeviceRecord : ITimestampedData
    {
        public string sourceDeviceId;
        public string dataPayloadJson;
        public long deviceTimestamp;
        public long receiveTimestamp;
        public long clockOffsetMs;
        public long timestampUnixMs;

        public long Timestamp => timestampUnixMs;
    }
}
