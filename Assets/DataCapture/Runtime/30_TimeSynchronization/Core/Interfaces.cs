using System;

namespace DataCapture.Synchronization
{
    /// <summary>
    /// All synchronized data must implement this interface.
    /// </summary>
    public interface ITimestampedData
    {
        long Timestamp { get; }
    }

    /// <summary>
    /// Standard interface for data sources.
    /// </summary>
    public interface IDataSource<T> where T : ITimestampedData
    {
        void RecordData(T data);
        T GetDataAt(long timestamp, long tolerance);
        T[] GetDataInRange(long startTime, long endTime);
    }

    /// <summary>
    /// Lightweight queue diagnostics used by timestamp synchronization.
    /// </summary>
    public interface IQueueHealth
    {
        int Count { get; }
        int Capacity { get; }
        long OldestTimestamp { get; }
        long NewestTimestamp { get; }
        long OverwriteCount { get; }
        long LastClearTimestamp { get; }
        long GenerationId { get; }
    }
}
