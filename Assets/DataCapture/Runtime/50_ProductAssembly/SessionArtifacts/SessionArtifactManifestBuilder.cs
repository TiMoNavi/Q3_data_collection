using System.IO;
using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Networking
{
    public sealed class SessionArtifactManifestBuilder : MonoBehaviour
    {
        [Header("Inputs")]
        [SerializeField] private Mp4ArtifactWriterStateSO mp4ArtifactWriterState;
        [SerializeField] private MetadataTimelineJournalSO metadataTimelineJournal;
        [SerializeField] private FrameIndexSO frameIndex;
        [SerializeField] private EncodedAccessUnitQueueSO encodedAccessUnitQueue;
        [SerializeField] private EncodingHealthStateSO encodingHealthState;
        [SerializeField] private NetworkSenderConfigurationSO networkConfiguration;

        [Header("Output")]
        [SerializeField] private SessionArtifactManifestSO sessionArtifactManifest;

        [Header("Runtime")]
        [SerializeField] private bool buildOnUpdate = true;
        [SerializeField] private bool buildOnlyWhenArtifactFinalized = true;
        [SerializeField] private string metadataTimelineFileName = "metadata_timeline.jsonl";
        [SerializeField] private string frameIndexFileName = "frame_index.json";
        [SerializeField] private string manifestFileName = "session_manifest.json";

        [Header("Diagnostics")]
        [SerializeField] private long lastBuiltFinalizedUnixMs;
        [SerializeField] private int buildCount;
        [SerializeField] private string lastStatus;

        private void Update()
        {
            if (buildOnUpdate)
            {
                BuildManifestIfReady();
            }
        }

        [ContextMenu("Build Session Artifact Manifest If Ready")]
        public bool BuildManifestIfReady()
        {
            if (!Validate(out string blocker))
            {
                lastStatus = blocker;
                return false;
            }

            if (buildOnlyWhenArtifactFinalized && !mp4ArtifactWriterState.finalized)
            {
                lastStatus = "MP4 artifact is not finalized.";
                return false;
            }

            if (mp4ArtifactWriterState.finalizedUnixMs > 0 &&
                mp4ArtifactWriterState.finalizedUnixMs == lastBuiltFinalizedUnixMs)
            {
                lastStatus = "Manifest already reflects the latest finalized artifact.";
                return false;
            }

            ApplyManifest();
            lastBuiltFinalizedUnixMs = mp4ArtifactWriterState.finalizedUnixMs;
            buildCount++;
            lastStatus = sessionArtifactManifest.IsPublishable
                ? "Built publishable session artifact manifest."
                : "Built incomplete session artifact manifest.";
            return sessionArtifactManifest.IsPublishable;
        }

        private void ApplyManifest()
        {
            string mp4Path = mp4ArtifactWriterState.outputPath ?? string.Empty;
            string baseDirectory = ResolveBaseDirectory(mp4Path);
            string metadataPath = ResolveMetadataTimelinePath(baseDirectory);
            string indexPath = ResolveFrameIndexPath(baseDirectory);
            string manifestPath = ResolveManifestPath(baseDirectory);

            long byteLength = ResolveByteLength(mp4Path);
            long frameCount = frameIndex != null && frameIndex.Count > 0
                ? frameIndex.Count
                : CountMetadataEntries();
            long accessUnitCount = encodedAccessUnitQueue != null
                ? encodedAccessUnitQueue.Count
                : 0;
            bool localFileExpected = networkConfiguration == null || networkConfiguration.UsesLocalFile;
            bool manifestRepresentsRequiredMp4 = !localFileExpected || !string.IsNullOrWhiteSpace(mp4Path);

            sessionArtifactManifest.sessionId = mp4ArtifactWriterState.sessionId ?? string.Empty;
            sessionArtifactManifest.mp4Path = mp4Path;
            sessionArtifactManifest.metadataTimelinePath = metadataPath;
            sessionArtifactManifest.frameIndexPath = indexPath;
            sessionArtifactManifest.manifestPath = manifestPath;
            sessionArtifactManifest.startedUnixMs = mp4ArtifactWriterState.startedUnixMs;
            sessionArtifactManifest.finalizedUnixMs = mp4ArtifactWriterState.finalizedUnixMs;
            sessionArtifactManifest.frameCount = frameCount;
            sessionArtifactManifest.accessUnitCount = accessUnitCount;
            sessionArtifactManifest.byteLength = byteLength;
            sessionArtifactManifest.hasFailure = HasFailure(localFileExpected, byteLength, frameCount);
            sessionArtifactManifest.isComplete = !sessionArtifactManifest.hasFailure &&
                mp4ArtifactWriterState.finalized &&
                manifestRepresentsRequiredMp4 &&
                frameCount > 0;
            sessionArtifactManifest.failureReason = ResolveFailureReason(localFileExpected, byteLength, frameCount);
        }

        private bool HasFailure(bool localFileExpected, long byteLength, long frameCount)
        {
            if (mp4ArtifactWriterState.hasFailure)
            {
                return true;
            }

            if (encodingHealthState != null && encodingHealthState.hasFailure)
            {
                return true;
            }

            if (localFileExpected && string.IsNullOrWhiteSpace(mp4ArtifactWriterState.outputPath))
            {
                return true;
            }

            if (localFileExpected && byteLength <= 0)
            {
                return true;
            }

            return frameCount <= 0;
        }

        private string ResolveFailureReason(bool localFileExpected, long byteLength, long frameCount)
        {
            if (mp4ArtifactWriterState.hasFailure)
            {
                return string.IsNullOrWhiteSpace(mp4ArtifactWriterState.lastFailureReason)
                    ? "MP4 artifact writer reports failure."
                    : mp4ArtifactWriterState.lastFailureReason;
            }

            if (encodingHealthState != null && encodingHealthState.hasFailure)
            {
                return string.IsNullOrWhiteSpace(encodingHealthState.lastFailureReason)
                    ? "Encoding health reports failure."
                    : encodingHealthState.lastFailureReason;
            }

            if (localFileExpected && string.IsNullOrWhiteSpace(mp4ArtifactWriterState.outputPath))
            {
                return "Local or hybrid mode expects an MP4 path, but none was published.";
            }

            if (localFileExpected && byteLength <= 0)
            {
                return "Local or hybrid mode expects a non-empty MP4 artifact.";
            }

            if (frameCount <= 0)
            {
                return "No frame index or metadata timeline entries were available.";
            }

            return string.Empty;
        }

        private long CountMetadataEntries()
        {
            return metadataTimelineJournal != null ? metadataTimelineJournal.Count : 0;
        }

        private long ResolveByteLength(string mp4Path)
        {
            if (mp4ArtifactWriterState.byteLength > 0)
            {
                return mp4ArtifactWriterState.byteLength;
            }

            if (!string.IsNullOrWhiteSpace(mp4Path) && File.Exists(mp4Path))
            {
                return new FileInfo(mp4Path).Length;
            }

            return 0;
        }

        private string ResolveBaseDirectory(string mp4Path)
        {
            if (!string.IsNullOrWhiteSpace(mp4Path))
            {
                string directory = Path.GetDirectoryName(mp4Path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    return directory;
                }
            }

            return Application.persistentDataPath;
        }

        private string ResolveMetadataTimelinePath(string baseDirectory)
        {
            if (!string.IsNullOrWhiteSpace(mp4ArtifactWriterState.metadataSidecarPath))
            {
                return mp4ArtifactWriterState.metadataSidecarPath;
            }

            return Path.Combine(baseDirectory, metadataTimelineFileName);
        }

        private string ResolveFrameIndexPath(string baseDirectory)
        {
            return Path.Combine(baseDirectory, frameIndexFileName);
        }

        private string ResolveManifestPath(string baseDirectory)
        {
            return Path.Combine(baseDirectory, manifestFileName);
        }

        private bool Validate(out string blocker)
        {
            if (mp4ArtifactWriterState == null)
            {
                blocker = "Mp4ArtifactWriterStateSO is not assigned.";
                return false;
            }

            if (sessionArtifactManifest == null)
            {
                blocker = "SessionArtifactManifestSO is not assigned.";
                return false;
            }

            blocker = string.Empty;
            return true;
        }
    }
}
