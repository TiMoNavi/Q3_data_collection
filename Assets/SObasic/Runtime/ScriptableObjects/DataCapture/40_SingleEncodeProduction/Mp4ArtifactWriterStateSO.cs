using UnityEngine;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "Mp4ArtifactWriterStateSO", menuName = "DataCapture/40 Single Encode Production/MP4 Artifact Writer State")]
    public class Mp4ArtifactWriterStateSO : ScriptableObject
    {
        public bool isRecording;
        public bool finalized;
        public bool hasFailure;
        public string sessionId;
        public string outputPath;
        public string metadataSidecarPath;
        public long startedUnixMs;
        public long finalizedUnixMs;
        public long byteLength;
        public int sampleCount;
        public string lastStatus;
        public string lastFailureReason;

        public bool IsUsableArtifact => finalized && !hasFailure && !string.IsNullOrWhiteSpace(outputPath);
    }
}
