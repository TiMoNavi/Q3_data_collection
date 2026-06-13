using UnityEngine;
using System.Net;
using System.Net.Sockets;

namespace DataCapture.Networking
{
    public class UdpPacketTransport : MonoBehaviour, INetworkSender
    {
        private enum StreamKind
        {
            Metadata,
            Video
        }

        [SerializeField] private NetworkSenderConfigurationSO configuration;
        [SerializeField] private StreamKind streamKind = StreamKind.Metadata;

        private UdpClient client;
        private IPEndPoint endpoint;
        private string lastHost;
        private int lastPort;

        public bool IsReady =>
            configuration != null &&
            configuration.UsesNetwork &&
            !string.IsNullOrWhiteSpace(configuration.ResolveHost());

        public bool TrySend(byte[] payload)
        {
            if (!IsReady || payload == null)
            {
                return false;
            }

            if (payload.Length > configuration.maxPacketBytes)
            {
                return false;
            }

            if (!EnsureClient())
            {
                return false;
            }

            try
            {
                return client.Send(payload, payload.Length, endpoint) == payload.Length;
            }
            catch (SocketException ex)
            {
                Debug.LogWarning("UDP send failed: " + ex.Message, this);
                return false;
            }
        }

        private void OnDisable()
        {
            CloseClient();
        }

        private bool EnsureClient()
        {
            int port = streamKind == StreamKind.Video
                ? configuration.ResolveVideoPort()
                : configuration.ResolveMetadataPort();
            string host = configuration.ResolveHost();
            if (client != null && endpoint != null && host == lastHost && port == lastPort)
            {
                return true;
            }

            CloseClient();

            try
            {
                endpoint = new IPEndPoint(Dns.GetHostAddresses(host)[0], port);
                client = new UdpClient();
                lastHost = host;
                lastPort = port;
                return true;
            }
            catch (SocketException ex)
            {
                Debug.LogWarning("UDP endpoint setup failed: " + ex.Message, this);
                endpoint = null;
                client = null;
                return false;
            }
        }

        private void CloseClient()
        {
            client?.Close();
            client = null;
            endpoint = null;
        }
    }
}
