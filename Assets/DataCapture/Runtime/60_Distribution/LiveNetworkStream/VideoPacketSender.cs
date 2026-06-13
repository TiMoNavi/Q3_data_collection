using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Networking
{
    public class VideoPacketSender : MonoBehaviour
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

        public bool Send(EncodedFrameRecord record, byte[] payload)
        {
            return SendDetailed(record, payload).Sent;
        }

        public NetworkSendResult SendDetailed(EncodedFrameRecord record, byte[] payload)
        {
            EncodedVideoPacketHeader header = EncodedVideoPacketHeader.FromRecord(record, nextSequenceId++);
            byte[] packetPayload = NetworkPacketEnvelope.PackJsonHeader(header, payload);

            if (configuration != null)
            {
                if (!configuration.UsesNetwork)
                {
                    return MarkSkipped("Network output target is local-only; video packet was not sent.");
                }

                if (!configuration.sendVideo)
                {
                    return MarkSkipped("Video stream is disabled in NetworkSenderConfigurationSO.");
                }
            }

            INetworkSender sender = Sender;
            if (sender == null || !sender.IsReady || !sender.TrySend(packetPayload))
            {
                return MarkFailed("Video packet sender is not ready or transport send failed.");
            }

            currentPacket?.SetHeader(header.header, packetPayload.Length);
            packetQueue?.Record(header.header);
            return MarkSent(packetPayload.Length);
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
