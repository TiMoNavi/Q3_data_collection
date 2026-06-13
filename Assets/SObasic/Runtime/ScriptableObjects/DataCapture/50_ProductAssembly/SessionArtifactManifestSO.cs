using UnityEngine;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "SessionArtifactManifestSO", menuName = "DataCapture/50 Product Assembly/Session Artifact Manifest")]
    public class SessionArtifactManifestSO : ScriptableObject
    {
        public string sessionId;
        public bool isComplete;
        public bool hasFailure;
        public string mp4Path;
        public string metadataTimelinePath;
        public string frameIndexPath;
        public string manifestPath;
        public long startedUnixMs;
        public long finalizedUnixMs;
        public long frameCount;
        public long accessUnitCount;
        public long byteLength;
        public string failureReason;

        public bool IsPublishable => isComplete && !hasFailure && !string.IsNullOrWhiteSpace(mp4Path);
    }
}
