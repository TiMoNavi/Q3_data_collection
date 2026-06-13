using UnityEngine;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "CurrentNetworkPacketSO", menuName = "DataCapture/50 Encoding Network/Current Network Packet")]
    public class CurrentNetworkPacketSO : ScriptableObject
    {
        public bool isValid;
        public string streamName;
        public long frameId;
        public long timestampUnixMs;
        public long sequenceId;
        public int byteLength;

        public void SetHeader(PacketTimestampHeader header, int byteLength)
        {
            streamName = header.streamName;
            frameId = header.frameId;
            timestampUnixMs = header.timestampUnixMs;
            sequenceId = header.sequenceId;
            this.byteLength = byteLength;
            isValid = timestampUnixMs > 0;
        }
    }
}
