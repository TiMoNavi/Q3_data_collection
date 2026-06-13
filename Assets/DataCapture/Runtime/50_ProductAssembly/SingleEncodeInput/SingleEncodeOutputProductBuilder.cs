using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Networking
{
    public sealed class SingleEncodeOutputProductBuilder : MonoBehaviour
    {
        [Header("05 Input From Stage 04")]
        [SerializeField] private SingleEncodeOutputQueueSO singleEncodeOutputQueue;

        [Header("05 Products")]
        [SerializeField] private RealtimeAlignedStreamQueueSO realtimeAlignedStreamQueue;
        [SerializeField] private SessionArtifactManifestSO sessionArtifactManifest;
        [SerializeField] private SessionFinalizeStateSO sessionFinalizeState;

        [Header("Runtime")]
        [SerializeField] private bool buildOnUpdate = true;

        [Header("Diagnostics")]
        [SerializeField] private long lastConsumedOutputId = -1;
        [SerializeField] private long nextRealtimeRecordId;
        [SerializeField] private int builtProductCount;
        [SerializeField] private string lastStatus;

        private void Update()
        {
            if (buildOnUpdate)
            {
                BuildLatestReadyOutput();
            }
        }

        [ContextMenu("Build Latest Ready Single Encode Output")]
        public bool BuildLatestReadyOutput()
        {
            if (singleEncodeOutputQueue == null)
            {
                lastStatus = "SingleEncodeOutputQueueSO is not assigned.";
                return false;
            }

            if (!singleEncodeOutputQueue.TryGetLatestReady(out SingleEncodeOutputRecord output))
            {
                lastStatus = "No ready single encode output is available.";
                return false;
            }

            if (output.outputId == lastConsumedOutputId)
            {
                lastStatus = "Single encode output was already consumed.";
                return false;
            }

            BuildRealtimeRecords(output);
            BuildManifest(output);
            EvaluateFinalizeState(output);

            lastConsumedOutputId = output.outputId;
            builtProductCount++;
            lastStatus = "Built 05 products from single encode output.";
            return true;
        }

        private void BuildRealtimeRecords(SingleEncodeOutputRecord output)
        {
            if (realtimeAlignedStreamQueue == null)
            {
                return;
            }

            if (output.frameIndexEntries != null && output.frameIndexEntries.Length > 0)
            {
                for (int i = 0; i < output.frameIndexEntries.Length; i++)
                {
                    FrameIndexEntry index = output.frameIndexEntries[i];
                    MetadataTimelineEntryRecord metadata = FindMetadataEntry(output, index.metadataTimelineEntryId, index.frameId);
                    realtimeAlignedStreamQueue.RecordData(new RealtimeAlignedStreamRecord
                    {
                        recordId = nextRealtimeRecordId++,
                        frameId = index.frameId,
                        timestampUnixMs = index.sourceTimestampUnixMs,
                        accessUnitId = index.accessUnitId,
                        metadataTimelineEntryId = index.metadataTimelineEntryId,
                        metadataEntry = metadata,
                        dropReason = metadata.dropReason ?? string.Empty
                    });
                }

                return;
            }

            if (output.timestampSamples == null)
            {
                return;
            }

            for (int i = 0; i < output.timestampSamples.Length; i++)
            {
                SingleEncodeTimestampSample sample = output.timestampSamples[i];
                MetadataTimelineEntryRecord metadata = FindMetadataEntry(output, sample.metadataTimelineEntryId, sample.frameId);
                realtimeAlignedStreamQueue.RecordData(new RealtimeAlignedStreamRecord
                {
                    recordId = nextRealtimeRecordId++,
                    frameId = sample.frameId,
                    timestampUnixMs = sample.sourceTimestampUnixMs,
                    accessUnitId = sample.accessUnitId,
                    metadataTimelineEntryId = sample.metadataTimelineEntryId,
                    metadataEntry = metadata,
                    dropReason = metadata.dropReason ?? string.Empty
                });
            }
        }

        private MetadataTimelineEntryRecord FindMetadataEntry(
            SingleEncodeOutputRecord output,
            long entryId,
            long frameId)
        {
            if (output.metadataTimelineEntries == null)
            {
                return default;
            }

            for (int i = output.metadataTimelineEntries.Length - 1; i >= 0; i--)
            {
                MetadataTimelineEntryRecord entry = output.metadataTimelineEntries[i];
                if ((entryId >= 0 && entry.entryId == entryId) ||
                    entry.frameId == frameId)
                {
                    return entry;
                }
            }

            return default;
        }

        private string ResolveFailureReason(SingleEncodeOutputRecord output)
        {
            if (!string.IsNullOrWhiteSpace(output.failureReason))
            {
                return output.failureReason;
            }

            if (!output.HasCompleteMetadataTimeline)
            {
                return "Stage 04 output does not include a complete metadata timeline.";
            }

            if (!output.HasCompleteFrameIndex)
            {
                return "Stage 04 output does not include a complete frame index.";
            }

            if (!output.HasCompleteVideoArtifact)
            {
                return "Stage 04 output does not include a complete video artifact.";
            }

            return string.Empty;
        }

        private bool IsPublishableOutput(SingleEncodeOutputRecord output)
        {
            return output.IsReady &&
                output.HasCompleteMetadataTimeline &&
                output.HasCompleteFrameIndex &&
                output.HasCompleteVideoArtifact;
        }

        private long ResolveFrameCount(SingleEncodeOutputRecord output)
        {
            if (output.frameIndexEntryCount > 0)
            {
                return output.frameIndexEntryCount;
            }

            if (output.metadataTimelineEntryCount > 0)
            {
                return output.metadataTimelineEntryCount;
            }

            return output.frameCount;
        }

        private long ResolveAccessUnitCount(SingleEncodeOutputRecord output)
        {
            if (output.accessUnitCount > 0)
            {
                return output.accessUnitCount;
            }

            if (output.frameIndexEntries == null)
            {
                return 0;
            }

            long count = 0;
            for (int i = 0; i < output.frameIndexEntries.Length; i++)
            {
                if (output.frameIndexEntries[i].accessUnitId >= 0)
                {
                    count++;
                }
            }

            return count;
        }

        private bool HasFailure(SingleEncodeOutputRecord output)
        {
            return !IsPublishableOutput(output);
        }

        private void BuildManifest(SingleEncodeOutputRecord output)
        {
            if (sessionArtifactManifest == null)
            {
                return;
            }

            sessionArtifactManifest.sessionId = output.sessionId;
            bool publishable = IsPublishableOutput(output);
            sessionArtifactManifest.isComplete = publishable;
            sessionArtifactManifest.hasFailure = !publishable;
            sessionArtifactManifest.mp4Path = output.videoArtifactPath;
            sessionArtifactManifest.metadataTimelinePath = output.metadataTimelinePath;
            sessionArtifactManifest.frameIndexPath = output.frameIndexPath;
            sessionArtifactManifest.startedUnixMs = output.startedUnixMs;
            sessionArtifactManifest.finalizedUnixMs = output.finalizedUnixMs;
            sessionArtifactManifest.frameCount = ResolveFrameCount(output);
            sessionArtifactManifest.accessUnitCount = ResolveAccessUnitCount(output);
            sessionArtifactManifest.byteLength = output.byteLength;
            sessionArtifactManifest.failureReason = ResolveFailureReason(output);
        }

        private void EvaluateFinalizeState(SingleEncodeOutputRecord output)
        {
            if (sessionFinalizeState == null)
            {
                return;
            }

            sessionFinalizeState.recordingEndedNormally = output.IsReady;
            sessionFinalizeState.metadataTimelineComplete = output.HasCompleteMetadataTimeline;
            sessionFinalizeState.frameIndexComplete = output.HasCompleteFrameIndex;
            sessionFinalizeState.videoArtifactComplete = output.HasCompleteVideoArtifact;
            sessionFinalizeState.mp4ArtifactComplete = output.HasCompleteVideoArtifact;
            sessionFinalizeState.encodingHealthy = !HasFailure(output);
            sessionFinalizeState.metadataTimelineBlocker = output.HasCompleteMetadataTimeline
                ? string.Empty
                : "Stage 04 output metadata timeline is empty.";
            sessionFinalizeState.frameIndexBlocker = output.HasCompleteFrameIndex
                ? string.Empty
                : "Stage 04 output frame index is empty.";
            sessionFinalizeState.mp4ArtifactBlocker = output.HasCompleteVideoArtifact
                ? string.Empty
                : "Stage 04 output video artifact is incomplete.";
            sessionFinalizeState.encodingBlocker = ResolveFailureReason(output);
            sessionFinalizeState.Evaluate();
        }
    }
}
