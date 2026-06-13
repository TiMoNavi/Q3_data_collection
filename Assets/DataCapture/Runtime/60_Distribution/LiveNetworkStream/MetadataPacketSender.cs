using System.Text;
using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Networking
{
    public class MetadataPacketSender : MonoBehaviour
    {
        [SerializeField] private NetworkSenderConfigurationSO configuration;
        [SerializeField] private MonoBehaviour senderBehaviour;
        [SerializeField] private CurrentNetworkPacketSO currentPacket;
        [SerializeField] private NetworkPacketQueueSO packetQueue;

        [Header("Runtime Diagnostics")]
        [SerializeField] private NetworkSendResult lastSendResult;
        [SerializeField] private int sentCount;
        [SerializeField] private int skippedCount;
        [SerializeField] private int failedCount;

        private long nextSequenceId;
        private INetworkSender Sender => senderBehaviour as INetworkSender;
        public NetworkSendResult LastSendResult => lastSendResult;

        public bool Send(MergedFrameSnapshotRecord snapshot)
        {
            return SendDetailed(snapshot).Sent;
        }

        public NetworkSendResult SendDetailed(MergedFrameSnapshotRecord snapshot)
        {
            MergedMetadataPacket packet = MergedMetadataPacket.FromSnapshot(snapshot, nextSequenceId++);
            byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet));

            if (configuration != null)
            {
                if (!configuration.UsesNetwork)
                {
                    return MarkSkipped("Network output target is local-only; metadata packet was not sent.");
                }

                if (!configuration.sendMetadata)
                {
                    return MarkSkipped("Metadata stream is disabled in NetworkSenderConfigurationSO.");
                }
            }

            INetworkSender sender = Sender;
            if (sender == null || !sender.IsReady || !sender.TrySend(payload))
            {
                return MarkFailed("Metadata packet sender is not ready or transport send failed.");
            }

            currentPacket?.SetHeader(packet.header, payload.Length);
            packetQueue?.Record(packet.header);
            return MarkSent(payload.Length);
        }

        private NetworkSendResult MarkSent(int byteLength)
        {
            sentCount++;
            lastSendResult = NetworkSendResult.SentBytes(byteLength);
            return lastSendResult;
        }

        private NetworkSendResult MarkSkipped(string reason)
        {
            skippedCount++;
            lastSendResult = NetworkSendResult.Skipped(reason);
            return lastSendResult;
        }

        private NetworkSendResult MarkFailed(string reason)
        {
            failedCount++;
            lastSendResult = NetworkSendResult.Failed(reason);
            return lastSendResult;
        }
    }
}
