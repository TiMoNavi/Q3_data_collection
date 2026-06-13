using UnityEngine;

namespace SObasic.CurrentQueueBridge
{
    [CreateAssetMenu(fileName = "RecordingToggleRequestSO", menuName = "DataCapture/Synchronization/Recording Toggle Request")]
    public class RecordingToggleRequestSO : ScriptableObject
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
