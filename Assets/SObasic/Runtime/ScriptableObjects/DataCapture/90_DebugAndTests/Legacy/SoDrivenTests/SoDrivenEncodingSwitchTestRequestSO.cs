using UnityEngine;

namespace DataCapture.Testing
{
    [CreateAssetMenu(fileName = "SoDrivenEncodingSwitchTestRequestSO", menuName = "DataCapture/90 Diagnostics/SO Driven Encoding Switch Test Request")]
    public class SoDrivenEncodingSwitchTestRequestSO : ScriptableObject
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
