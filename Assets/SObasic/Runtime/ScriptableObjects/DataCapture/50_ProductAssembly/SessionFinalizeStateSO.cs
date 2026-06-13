using UnityEngine;

namespace DataCapture.Networking
{
    public enum SessionFinalizeDecision
    {
        Pending,
        Publish,
        Discard,
        Quarantine
    }

    [CreateAssetMenu(fileName = "SessionFinalizeStateSO", menuName = "DataCapture/50 Product Assembly/Session Finalize State")]
    public class SessionFinalizeStateSO : ScriptableObject, SObasic.IActiveState
    {
        public SessionFinalizeDecision decision = SessionFinalizeDecision.Pending;
        public DataCaptureSessionMode sessionMode = DataCaptureSessionMode.LocalOnly;
        public StreamOutputTarget outputTarget = StreamOutputTarget.LocalFile;
        public bool realtimeStreamRequired;
        public bool mp4ArtifactRequired = true;
        public bool recordingEndedNormally;
        public bool metadataTimelineComplete;
        public bool frameIndexComplete;
        public bool realtimeStreamComplete;
        public bool mp4ArtifactComplete;
        public bool requiredProductsComplete;
        public bool videoArtifactComplete;
        public bool encodingHealthy;
        public string activeBlocker = "Session has not finalized.";
        public string modeBlocker;
        public string realtimeStreamBlocker;
        public string mp4ArtifactBlocker;
        public string metadataTimelineBlocker;
        public string frameIndexBlocker;
        public string encodingBlocker;
        public string recordingBlocker;
        public long lastUpdatedUnixMs;

        public bool Active => decision == SessionFinalizeDecision.Publish;

        public void Evaluate()
        {
            requiredProductsComplete =
                (!realtimeStreamRequired || realtimeStreamComplete) &&
                (!mp4ArtifactRequired || mp4ArtifactComplete);
            videoArtifactComplete = requiredProductsComplete;

            bool publish = recordingEndedNormally &&
                metadataTimelineComplete &&
                frameIndexComplete &&
                requiredProductsComplete &&
                encodingHealthy;

            decision = publish ? SessionFinalizeDecision.Publish : SessionFinalizeDecision.Quarantine;
            activeBlocker = publish ? string.Empty : "Session artifact is incomplete or unhealthy.";
            lastUpdatedUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
