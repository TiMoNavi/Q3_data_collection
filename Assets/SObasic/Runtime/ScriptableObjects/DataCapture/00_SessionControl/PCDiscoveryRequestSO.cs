using UnityEngine;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "PCDiscoveryRequestSO", menuName = "DataCapture/50 Encoding Network/PC Discovery Request")]
    public class PCDiscoveryRequestSO : ScriptableObject
    {
        public bool requested;
        public int requestRevision;
        public long requestedAtUnixMs;
        public string requestSource = string.Empty;

        public void Request(string source)
        {
            requested = true;
            requestRevision++;
            requestedAtUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            requestSource = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
        }

        public void Clear()
        {
            requested = false;
        }
    }
}
