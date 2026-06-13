using DataCapture.Synchronization;

namespace DataCapture.Networking
{
    [System.Serializable]
    public class MergedMetadataPacket
    {
        public PacketTimestampHeader header;
        public MergedFrameSnapshotRecord snapshot;

        public static MergedMetadataPacket FromSnapshot(MergedFrameSnapshotRecord snapshot, long sequenceId)
        {
            return new MergedMetadataPacket
            {
                header = new PacketTimestampHeader
                {
                    frameId = snapshot.frameId,
                    timestampUnixMs = snapshot.timestampUnixMs,
                    sequenceId = sequenceId,
                    streamName = "metadata"
                },
                snapshot = snapshot
            };
        }
    }
}
