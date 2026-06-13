using System;

namespace DataCapture.Synchronization
{
    public static class SynchronizationClock
    {
        public static long GetUnixMilliseconds(TimeStampVariable timestampVariable)
        {
            if (timestampVariable != null && timestampVariable.currentTimestamp > 0)
            {
                return timestampVariable.currentTimestamp;
            }

            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
