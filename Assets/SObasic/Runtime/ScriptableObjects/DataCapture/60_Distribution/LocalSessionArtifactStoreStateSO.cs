using UnityEngine;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "LocalSessionArtifactStoreStateSO", menuName = "DataCapture/60 Distribution/Local Session Artifact Store State")]
    public class LocalSessionArtifactStoreStateSO : ScriptableObject
    {
        public bool isActive = true;
        public bool hasStoredLatestArtifact;
        public bool hasFailure;
        public string lastSessionId;
        public string lastStoredManifestPath;
        public string lastStoredMp4Path;
        public string lastFailureReason;
        public long lastStoredUnixMs;
        public int storedArtifactCount;
    }
}
