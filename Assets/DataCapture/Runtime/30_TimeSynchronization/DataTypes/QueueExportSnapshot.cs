namespace DataCapture.Synchronization
{
    [System.Serializable]
    public class QueueExportSnapshot<T> where T : ITimestampedData
    {
        public string queueName;
        public long exportedAtUnixMs;
        public int count;
        public T[] records;

        public QueueExportSnapshot(string queueName, long exportedAtUnixMs, T[] records)
        {
            this.queueName = queueName;
            this.exportedAtUnixMs = exportedAtUnixMs;
            this.records = records ?? new T[0];
            count = this.records.Length;
        }
    }
}
