using System;
using DataCapture.Synchronization;

namespace SObasic.CurrentQueueBridge
{
    public interface ICurrentRecordSource
    {
        string SourceName { get; }
        Type RecordType { get; }
        bool IsRecordValid { get; }
        long CurrentTimestampUnixMs { get; }
        long RecordSequence { get; }
        bool TryGetRecord(out ITimestampedData record);
    }
}
