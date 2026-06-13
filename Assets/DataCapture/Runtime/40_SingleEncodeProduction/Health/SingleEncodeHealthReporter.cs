using UnityEngine;

namespace DataCapture.Networking
{
    public sealed class SingleEncodeHealthReporter : MonoBehaviour
    {
        [Header("Inputs")]
        [SerializeField] private CurrentVideoFrameInputSO currentVideoFrameInput;
        [SerializeField] private EncodedAccessUnitQueueSO encodedAccessUnitQueue;
        [SerializeField] private Mp4ArtifactWriterStateSO mp4ArtifactWriterState;

        [Header("Output")]
        [SerializeField] private EncodingHealthStateSO encodingHealthState;

        [Header("Runtime")]
        [SerializeField] private bool reportOnUpdate = true;
        [SerializeField] private bool allowLocalMp4WithoutAccessUnits = true;

        [Header("Diagnostics")]
        [SerializeField] private string lastStatus;

        private void Update()
        {
            if (reportOnUpdate)
            {
                Report();
            }
        }

        [ContextMenu("Report Single Encode Health")]
        public bool Report()
        {
            if (encodingHealthState == null)
            {
                lastStatus = "EncodingHealthStateSO is not assigned.";
                return false;
            }

            if (currentVideoFrameInput == null || !currentVideoFrameInput.isValid || currentVideoFrameInput.inputTexture == null)
            {
                MarkBlocker("Current video frame input is not ready.");
                return false;
            }

            if (encodedAccessUnitQueue != null && encodedAccessUnitQueue.Count > 0)
            {
                EncodedAccessUnitRecord[] accessUnits = encodedAccessUnitQueue.ExportSnapshot();
                EncodedAccessUnitRecord latest = accessUnits[accessUnits.Length - 1];
                encodingHealthState.MarkAccessUnit(
                    latest.frameId,
                    latest.sourceTimestampUnixMs,
                    latest.encodedPtsUs);
                lastStatus = "Encoded access unit output is healthy.";
                return true;
            }

            if (allowLocalMp4WithoutAccessUnits &&
                mp4ArtifactWriterState != null &&
                (mp4ArtifactWriterState.isRecording || mp4ArtifactWriterState.IsUsableArtifact))
            {
                encodingHealthState.encoderInitialized = true;
                encodingHealthState.inputTextureReady = true;
                encodingHealthState.latestAccessUnitReady = false;
                encodingHealthState.hasFailure = false;
                encodingHealthState.activeBlocker = mp4ArtifactWriterState.isRecording
                    ? "Local MP4 bootstrap is recording without access-unit bus."
                    : string.Empty;
                encodingHealthState.latestFrameId = currentVideoFrameInput.sourceCameraFrameId;
                encodingHealthState.latestSourceTimestampUnixMs = currentVideoFrameInput.timestampUnixMs;
                encodingHealthState.latestEncodedPtsUs = mp4ArtifactWriterState.sampleCount;
                encodingHealthState.lastFailureReason = string.Empty;
                encodingHealthState.lastUpdatedUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                lastStatus = mp4ArtifactWriterState.isRecording
                    ? "Local MP4 bootstrap is recording."
                    : "Local MP4 bootstrap artifact is usable.";
                return true;
            }

            MarkBlocker("No encoded access units or local MP4 artifact are available.");
            return false;
        }

        private void MarkBlocker(string reason)
        {
            encodingHealthState.encoderInitialized = false;
            encodingHealthState.inputTextureReady = currentVideoFrameInput != null &&
                currentVideoFrameInput.isValid &&
                currentVideoFrameInput.inputTexture != null;
            encodingHealthState.latestAccessUnitReady = false;
            encodingHealthState.hasFailure = false;
            encodingHealthState.activeBlocker = reason;
            encodingHealthState.lastUpdatedUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lastStatus = reason;
        }
    }
}
