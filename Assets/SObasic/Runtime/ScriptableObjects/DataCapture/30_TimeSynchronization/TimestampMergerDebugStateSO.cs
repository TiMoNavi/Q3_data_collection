using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Diagnostics
{
    [CreateAssetMenu(fileName = "TimestampMergerDebugStateSO", menuName = "DataCapture/90 Diagnostics/Timestamp Merger Debug State")]
    public class TimestampMergerDebugStateSO : ScriptableObject
    {
        public long latestCameraFrameId;
        public long latestTimestampUnixMs;
        public int mergedCount;
        public bool latestHasCameraImage;
        public bool latestHasCameraPose;
        public bool latestHasCameraMetadata;
        public bool latestHasCameraStreamState;
        public bool latestHasController;
        public bool latestHasVirtualLayer;
        public long latestCameraImageDeltaMs;
        public long latestCameraPoseDeltaMs;
        public long latestCameraMetadataDeltaMs;
        public long latestCameraStreamStateDeltaMs;
        public long latestControllerDeltaMs;
        public long latestVirtualLayerDeltaMs;
        public MergedFrameStatus latestStatus;
        public bool latestIsSendable;
        public MergedFrameStreamMask latestRequiredStreamMask;
        public MergedFrameStreamMask latestMissingRequiredStreamMask;
        public MergedFrameStreamMask latestMatchedStreamMask;
        public string latestDropReason;
        public string statusMessage;

        [Header("Runtime Diagnostics")]
        public bool logStatusChangesToUnity = true;
        public string lastDebugMessage;
        public long lastChangedUnixMs;
        public int statusChangeCount;
        public string[] recentDebugEvents = new string[16];

        public void SetLatest(
            long cameraFrameId,
            long timestampUnixMs,
            int mergedCount,
            bool hasCameraImage,
            bool hasCameraPose,
            bool hasCameraMetadata,
            bool hasCameraStreamState,
            bool hasController,
            bool hasVirtualLayer,
            long cameraImageDeltaMs,
            long cameraPoseDeltaMs,
            long cameraMetadataDeltaMs,
            long cameraStreamStateDeltaMs,
            long controllerDeltaMs,
            long virtualLayerDeltaMs,
            MergedFrameStatus status,
            bool isSendable,
            MergedFrameStreamMask requiredStreamMask,
            MergedFrameStreamMask missingRequiredStreamMask,
            MergedFrameStreamMask matchedStreamMask,
            string dropReason,
            string statusMessage)
        {
            MergedFrameStatus previousStatus = latestStatus;
            bool previousIsSendable = latestIsSendable;
            MergedFrameStreamMask previousMissingMask = latestMissingRequiredStreamMask;
            string previousDropReason = latestDropReason;

            latestCameraFrameId = cameraFrameId;
            latestTimestampUnixMs = timestampUnixMs;
            this.mergedCount = mergedCount;
            latestHasCameraImage = hasCameraImage;
            latestHasCameraPose = hasCameraPose;
            latestHasCameraMetadata = hasCameraMetadata;
            latestHasCameraStreamState = hasCameraStreamState;
            latestHasController = hasController;
            latestHasVirtualLayer = hasVirtualLayer;
            latestCameraImageDeltaMs = cameraImageDeltaMs;
            latestCameraPoseDeltaMs = cameraPoseDeltaMs;
            latestCameraMetadataDeltaMs = cameraMetadataDeltaMs;
            latestCameraStreamStateDeltaMs = cameraStreamStateDeltaMs;
            latestControllerDeltaMs = controllerDeltaMs;
            latestVirtualLayerDeltaMs = virtualLayerDeltaMs;
            latestStatus = status;
            latestIsSendable = isSendable;
            latestRequiredStreamMask = requiredStreamMask;
            latestMissingRequiredStreamMask = missingRequiredStreamMask;
            latestMatchedStreamMask = matchedStreamMask;
            latestDropReason = dropReason;
            this.statusMessage = statusMessage;

            if (previousStatus != latestStatus ||
                previousIsSendable != latestIsSendable ||
                previousMissingMask != latestMissingRequiredStreamMask ||
                previousDropReason != latestDropReason)
            {
                string message =
                    "TimestampMerger status=" + latestStatus +
                    " sendable=" + latestIsSendable +
                    " frame=" + latestCameraFrameId +
                    " mergedCount=" + this.mergedCount +
                    " missing=" + latestMissingRequiredStreamMask;

                if (!string.IsNullOrWhiteSpace(latestDropReason))
                {
                    message += " reason=" + latestDropReason;
                }

                AppendDebugEvent(message, !latestIsSendable);
            }
        }

        [ContextMenu("Log Runtime Diagnostics")]
        public void LogRuntimeDiagnostics()
        {
            Debug.Log(
                "TimestampMerger diagnostics: status=" + latestStatus +
                " sendable=" + latestIsSendable +
                " frame=" + latestCameraFrameId +
                " mergedCount=" + mergedCount +
                " missing=" + latestMissingRequiredStreamMask +
                " dropReason=" + latestDropReason +
                " lastDebug=" + lastDebugMessage,
                this);
        }

        private void AppendDebugEvent(string message, bool warning)
        {
            lastDebugMessage = message;
            lastChangedUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            statusChangeCount++;

            if (recentDebugEvents == null || recentDebugEvents.Length == 0)
            {
                recentDebugEvents = new string[16];
            }

            for (int i = recentDebugEvents.Length - 1; i > 0; i--)
            {
                recentDebugEvents[i] = recentDebugEvents[i - 1];
            }

            recentDebugEvents[0] = lastChangedUnixMs + " " + message;

            if (!logStatusChangesToUnity)
            {
                return;
            }

            if (warning)
            {
                Debug.LogWarning(message, this);
            }
            else
            {
                Debug.Log(message, this);
            }
        }
    }
}
