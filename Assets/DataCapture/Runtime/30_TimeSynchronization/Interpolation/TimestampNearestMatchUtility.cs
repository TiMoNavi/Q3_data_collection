using System;

namespace DataCapture.Synchronization
{
    public static class TimestampNearestMatchUtility
    {
        public static FrameMatchStatus GetMatchStatus(long targetTimestamp, long matchedTimestamp, long toleranceMs)
        {
            if (matchedTimestamp <= 0)
            {
                return FrameMatchStatus.Missing;
            }

            long delta = Math.Abs(matchedTimestamp - targetTimestamp);
            if (delta == 0)
            {
                return FrameMatchStatus.Exact;
            }

            return delta <= toleranceMs ? FrameMatchStatus.WithinTolerance : FrameMatchStatus.OutsideTolerance;
        }

        public static long GetDeltaMs(long targetTimestamp, long matchedTimestamp)
        {
            return matchedTimestamp > 0 ? Math.Abs(matchedTimestamp - targetTimestamp) : -1;
        }

        public static bool IsWithinTolerance(long targetTimestamp, long matchedTimestamp, long toleranceMs)
        {
            return GetMatchStatus(targetTimestamp, matchedTimestamp, toleranceMs) != FrameMatchStatus.Missing &&
                   GetMatchStatus(targetTimestamp, matchedTimestamp, toleranceMs) != FrameMatchStatus.OutsideTolerance;
        }
    }
}
