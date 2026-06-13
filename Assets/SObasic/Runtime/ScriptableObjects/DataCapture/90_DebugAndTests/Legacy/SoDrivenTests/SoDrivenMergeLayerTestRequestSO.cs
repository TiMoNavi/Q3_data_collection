using UnityEngine;

namespace DataCapture.Testing
{
    [CreateAssetMenu(fileName = "SoDrivenMergeLayerTestRequestSO", menuName = "DataCapture/90 Diagnostics/SO Driven Merge Layer Test Request")]
    public class SoDrivenMergeLayerTestRequestSO : ScriptableObject
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
