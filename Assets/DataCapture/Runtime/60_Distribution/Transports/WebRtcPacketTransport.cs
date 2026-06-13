using UnityEngine;

namespace DataCapture.Networking
{
    public class WebRtcPacketTransport : MonoBehaviour, INetworkSender
    {
        public bool IsReady => false;

        public bool TrySend(byte[] payload)
        {
            return false;
        }
    }
}
