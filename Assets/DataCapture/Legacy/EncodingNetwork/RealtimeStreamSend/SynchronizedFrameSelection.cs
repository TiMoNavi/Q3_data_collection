using DataCapture.Synchronization;

namespace DataCapture.Networking
{
    public static class SynchronizedFrameSelection
    {
        public static bool TryGetLatestSendableSnapshot(
            MergedFrameSnapshotQueueSO mergedQueue,
            out MergedFrameSnapshotRecord snapshot)
        {
            snapshot = default;
            if (mergedQueue == null)
            {
                return false;
            }

            MergedFrameSnapshotRecord[] records = mergedQueue.ExportSnapshot();
            for (int i = records.Length - 1; i >= 0; i--)
            {
                if (records[i].isSendable)
                {
                    snapshot = records[i];
                    return true;
                }
            }

            return false;
        }

        public static bool HasSendableSnapshotForFrame(
            MergedFrameSnapshotQueueSO mergedQueue,
            CameraImageFrameRecord sourceFrame)
        {
            if (mergedQueue == null)
            {
                return false;
            }

            MergedFrameSnapshotRecord[] records = mergedQueue.ExportSnapshot();
            for (int i = records.Length - 1; i >= 0; i--)
            {
                MergedFrameSnapshotRecord snapshot = records[i];
                if (snapshot.frameId == sourceFrame.frameId)
                {
                    return snapshot.isSendable;
                }

                if (snapshot.timestampUnixMs < sourceFrame.timestampUnixMs)
                {
                    break;
                }
            }

            return false;
        }
    }
}
