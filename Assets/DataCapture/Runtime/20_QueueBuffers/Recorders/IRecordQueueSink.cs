using System;
using DataCapture.Synchronization;

namespace SObasic.CurrentQueueBridge
{
    public interface IRecordQueueSink
    {
        string QueueName { get; }
        Type RecordType { get; }
        int Count { get; }
        bool TryRecord(ITimestampedData record);
        void ClearQueue();
    }
}
