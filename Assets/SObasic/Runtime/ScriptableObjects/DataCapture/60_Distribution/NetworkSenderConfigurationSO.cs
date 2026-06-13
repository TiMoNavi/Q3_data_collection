using System;
using UnityEngine;

namespace DataCapture.Networking
{
    public enum StreamOutputTarget
    {
        RemoteReceiver,
        SelfReceiver,
        LocalFile,
        RemoteAndLocalFile,
        SelfAndLocalFile
    }

    [CreateAssetMenu(fileName = "NetworkSenderConfigurationSO", menuName = "DataCapture/50 Encoding Network/Network Sender Configuration")]
    public class NetworkSenderConfigurationSO : ScriptableObject
    {
        [Header("Discovery")]
        public bool enableLanDiscovery = true;
        public string discoveryMagic = "Q3DC_DISCOVER";
        public int discoveryPort = 49000;
        public int protocolVersion = 1;

        [Header("Discovery Strategy")]
        public bool includeLimitedBroadcast = true;
        public bool includeDirectedBroadcasts = true;
        public bool avoidVpnTunnelInterfaces = true;
        public string[] additionalDiscoveryTargets = Array.Empty<string>();

        [Header("Editor Discovery Override")]
        public string editorLocalBindAddress = string.Empty;
        public string editorDiscoveryBroadcastAddress = string.Empty;

        [Header("Destination")]
        public string remoteHost = string.Empty;
        public int videoPort = 5000;
        public int metadataPort = 5001;
        public int maxPacketBytes = 60000;

        [Header("Output Target")]
        public StreamOutputTarget outputTarget = StreamOutputTarget.RemoteReceiver;
        public string selfHost = "127.0.0.1";
        public int selfVideoPort = 5100;
        public int selfMetadataPort = 5101;
        public bool localSaveEnabled;
        public string localSaveDirectory = "DataCaptureVideo";
        public string localContainerFormat = "H264AnnexB";

        [Header("Streams")]
        public bool sendVideo = true;
        public bool sendMetadata = true;

        [Header("Diagnostics")]
        public bool receiverDiscovered;
        public string lastDiscoveryStatus;
        public long lastDiscoveryUnixMs;
        public string lastDiscoveryTargets;
        public string localNetworkSummary;
        public string networkWarning;
        public bool vpnOrTunnelInterfaceDetected;
        public int discoveryTargetCount;
        public int discoveryCandidateInterfaceCount;
        public int ignoredVpnInterfaceCount;

        public bool UsesNetwork =>
            outputTarget == StreamOutputTarget.RemoteReceiver ||
            outputTarget == StreamOutputTarget.SelfReceiver ||
            outputTarget == StreamOutputTarget.RemoteAndLocalFile ||
            outputTarget == StreamOutputTarget.SelfAndLocalFile;

        public bool UsesLocalFile =>
            localSaveEnabled ||
            outputTarget == StreamOutputTarget.LocalFile ||
            outputTarget == StreamOutputTarget.RemoteAndLocalFile ||
            outputTarget == StreamOutputTarget.SelfAndLocalFile;

        public bool UsesSelfReceiver =>
            outputTarget == StreamOutputTarget.SelfReceiver ||
            outputTarget == StreamOutputTarget.SelfAndLocalFile;

        public string ResolveHost()
        {
            return UsesSelfReceiver ? selfHost : remoteHost;
        }

        public int ResolveVideoPort()
        {
            return UsesSelfReceiver ? selfVideoPort : videoPort;
        }

        public int ResolveMetadataPort()
        {
            return UsesSelfReceiver ? selfMetadataPort : metadataPort;
        }

        private void OnValidate()
        {
            discoveryPort = Mathf.Clamp(discoveryPort, 1, 65535);
            videoPort = Mathf.Clamp(videoPort, 1, 65535);
            metadataPort = Mathf.Clamp(metadataPort, 1, 65535);
            selfVideoPort = Mathf.Clamp(selfVideoPort, 1, 65535);
            selfMetadataPort = Mathf.Clamp(selfMetadataPort, 1, 65535);
            maxPacketBytes = Mathf.Clamp(maxPacketBytes, 512, 65000);

            if (string.IsNullOrWhiteSpace(selfHost))
            {
                selfHost = "127.0.0.1";
            }

            if (string.IsNullOrWhiteSpace(localSaveDirectory))
            {
                localSaveDirectory = "DataCaptureVideo";
            }

            if (additionalDiscoveryTargets == null)
            {
                additionalDiscoveryTargets = Array.Empty<string>();
            }
        }
    }
}
