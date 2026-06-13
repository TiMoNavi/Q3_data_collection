using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Networking
{
    public sealed class SingleEncodeStageBoundary : MonoBehaviour
    {
        [Header("04 Unified Input")]
        [SerializeField] private MergedFrameSnapshotQueueSO synchronizedFrameQueue;
        [SerializeField] private MetadataTimelineJournalSO metadataTimelineJournal;

        [Header("04 Internal Products")]
        [SerializeField] private EncodedAccessUnitQueueSO encodedAccessUnitQueue;
        [SerializeField] private Mp4ArtifactWriterStateSO mp4ArtifactWriterState;
        [SerializeField] private FrameIndexSO frameIndex;
        [SerializeField] private EncodingHealthStateSO encodingHealthState;

        [Header("04 Unified Output")]
        [SerializeField] private SingleEncodeOutputQueueSO outputQueue;

        [Header("Runtime")]
        [SerializeField] private bool publishOnUpdate = true;
        [SerializeField] private bool publishOnlyFinalizedArtifacts = true;
        [SerializeField] private int maxTimestampSamples = 600;
        [SerializeField] private string frameIndexFileName = "frame_index.json";

        [Header("Diagnostics")]
        [SerializeField] private long nextOutputId;
        [SerializeField] private long lastPublishedGenerationId = -1;
        [SerializeField] private int publishedCount;
        [SerializeField] private string lastStatus;

        private void Update()
        {
            if (publishOnUpdate)
            {
                PublishIfReady();
            }
        }

        [ContextMenu("Publish Single Encode Output If Ready")]
        public bool PublishIfReady()
        {
            if (!ValidateInputs(out string blocker))
            {
                lastStatus = blocker;
                return false;
            }

            if (publishOnlyFinalizedArtifacts && !mp4ArtifactWriterState.finalized)
            {
                lastStatus = "MP4 artifact is not finalized.";
                return false;
            }

            long outputGeneration = GetOutputGeneration();
            if (outputGeneration == lastPublishedGenerationId)
            {
                lastStatus = "Single encode output generation was already published.";
                return false;
            }

            SingleEncodeOutputRecord record = BuildOutputRecord();
            outputQueue.RecordData(record);
            lastPublishedGenerationId = outputGeneration;
            publishedCount++;
            lastStatus = record.IsReady
                ? "Published single encode output."
                : "Published failed single encode output.";
            return record.IsReady;
        }

        private bool ValidateInputs(out string blocker)
        {
            if (synchronizedFrameQueue == null)
            {
                blocker = "MergedFrameSnapshotQueueSO is not assigned.";
                return false;
            }

            if (encodedAccessUnitQueue == null)
            {
                blocker = "EncodedAccessUnitQueueSO is not assigned.";
                return false;
            }

            if (metadataTimelineJournal == null)
            {
                blocker = "MetadataTimelineJournalSO is not assigned.";
                return false;
            }

            if (mp4ArtifactWriterState == null)
            {
                blocker = "Mp4ArtifactWriterStateSO is not assigned.";
                return false;
            }

            if (frameIndex == null)
            {
                blocker = "FrameIndexSO is not assigned.";
                return false;
            }

            if (encodingHealthState == null)
            {
                blocker = "EncodingHealthStateSO is not assigned.";
                return false;
            }

            if (outputQueue == null)
            {
                blocker = "SingleEncodeOutputQueueSO is not assigned.";
                return false;
            }

            blocker = string.Empty;
            return true;
        }

        private long GetOutputGeneration()
        {
            unchecked
            {
                long generation = 17;
                generation = generation * 31 + synchronizedFrameQueue.GenerationId;
                generation = generation * 31 + metadataTimelineJournal.GenerationId;
                generation = generation * 31 + encodedAccessUnitQueue.GenerationId;
                generation = generation * 31 + frameIndex.Count;
                generation = generation * 31 + mp4ArtifactWriterState.finalizedUnixMs;
                generation = generation * 31 + (encodingHealthState.hasFailure ? 1 : 0);
                return generation;
            }
        }

        private SingleEncodeOutputRecord BuildOutputRecord()
        {
            EncodedAccessUnitRecord[] accessUnits = encodedAccessUnitQueue.ExportSnapshot();
            MergedFrameSnapshotRecord[] synchronizedInputs = synchronizedFrameQueue.ExportSendableSnapshot();
            MetadataTimelineEntryRecord[] metadataEntries = metadataTimelineJournal.ExportSnapshot();
            FrameIndexEntry[] indexEntries = ResolveFrameIndexEntries(accessUnits, metadataEntries, synchronizedInputs);
            SingleEncodeTimestampSample[] timestampSamples = BuildTimestampSamples(accessUnits, indexEntries, metadataEntries);

            bool hasFailure = encodingHealthState.hasFailure || mp4ArtifactWriterState.hasFailure;
            string failureReason = ResolveFailureReason(
                accessUnits,
                synchronizedInputs,
                metadataEntries,
                indexEntries,
                timestampSamples);

            long firstFrameId = ResolveFirstFrameId(indexEntries, metadataEntries, synchronizedInputs);
            long lastFrameId = ResolveLastFrameId(indexEntries, metadataEntries, synchronizedInputs);
            long timestampStart = ResolveTimestampStart(indexEntries, metadataEntries, timestampSamples);
            long timestampEnd = ResolveTimestampEnd(indexEntries, metadataEntries, timestampSamples);

            return new SingleEncodeOutputRecord
            {
                outputId = nextOutputId++,
                status = hasFailure || !string.IsNullOrWhiteSpace(failureReason)
                    ? SingleEncodeOutputStatus.Failed
                    : SingleEncodeOutputStatus.Ready,
                artifactKind = mp4ArtifactWriterState.IsUsableArtifact
                    ? SingleEncodeVideoArtifactKind.Mp4File
                    : SingleEncodeVideoArtifactKind.EncodedAccessUnitSequence,
                sessionId = mp4ArtifactWriterState.sessionId ?? string.Empty,
                codec = ResolveCodec(accessUnits),
                width = ResolveWidth(accessUnits),
                height = ResolveHeight(accessUnits),
                frameRate = ResolveFrameRate(timestampSamples),
                videoArtifactPath = mp4ArtifactWriterState.outputPath ?? string.Empty,
                metadataTimelinePath = mp4ArtifactWriterState.metadataSidecarPath ?? string.Empty,
                frameIndexPath = ResolveFrameIndexPath(),
                byteLength = ResolveByteLength(accessUnits),
                startedUnixMs = ResolveStartUnixMs(timestampStart),
                finalizedUnixMs = ResolveFinalizedUnixMs(timestampEnd),
                firstFrameId = firstFrameId,
                lastFrameId = lastFrameId,
                timestampStartUnixMs = timestampStart,
                timestampEndUnixMs = timestampEnd,
                frameCount = ResolveFrameCount(synchronizedInputs, indexEntries, metadataEntries, accessUnits),
                accessUnitCount = accessUnits.Length,
                timestampSampleCount = timestampSamples.Length,
                metadataTimelineEntryCount = metadataEntries.Length,
                frameIndexEntryCount = indexEntries.Length,
                videoArtifactComplete = mp4ArtifactWriterState.IsUsableArtifact || accessUnits.Length > 0,
                timestampSamples = timestampSamples,
                metadataTimelineEntries = metadataEntries,
                frameIndexEntries = indexEntries,
                failureReason = failureReason
            };
        }

        private SingleEncodeTimestampSample[] BuildTimestampSamples(
            EncodedAccessUnitRecord[] accessUnits,
            FrameIndexEntry[] indexEntries,
            MetadataTimelineEntryRecord[] metadataEntries)
        {
            int sourceCount = indexEntries.Length;
            if (sourceCount == 0)
            {
                sourceCount = metadataEntries.Length;
            }

            int count = Mathf.Min(sourceCount, Mathf.Max(1, maxTimestampSamples));
            var samples = new SingleEncodeTimestampSample[count];
            int start = Mathf.Max(0, sourceCount - count);

            for (int i = 0; i < count; i++)
            {
                int sourceIndex = start + i;
                FrameIndexEntry index = sourceIndex < indexEntries.Length
                    ? indexEntries[sourceIndex]
                    : ResolveIndexFromMetadata(metadataEntries[sourceIndex]);
                samples[i] = new SingleEncodeTimestampSample
                {
                    frameId = index.frameId,
                    sourceTimestampUnixMs = index.sourceTimestampUnixMs,
                    encodedPtsUs = index.encodedPtsUs,
                    accessUnitId = index.accessUnitId,
                    mp4SampleIndex = index.mp4SampleIndex,
                    metadataTimelineEntryId = index.metadataTimelineEntryId
                };
            }

            return samples;
        }

        private FrameIndexEntry[] ResolveFrameIndexEntries(
            EncodedAccessUnitRecord[] accessUnits,
            MetadataTimelineEntryRecord[] metadataEntries,
            MergedFrameSnapshotRecord[] synchronizedInputs)
        {
            FrameIndexEntry[] existingEntries = frameIndex.Entries ?? new FrameIndexEntry[0];
            if (existingEntries.Length > 0)
            {
                return existingEntries;
            }

            if (accessUnits.Length > 0)
            {
                return BuildFrameIndexFromAccessUnits(accessUnits, metadataEntries);
            }

            if (metadataEntries.Length > 0)
            {
                return BuildFrameIndexFromMetadata(metadataEntries);
            }

            return BuildFrameIndexFromSnapshots(synchronizedInputs);
        }

        private FrameIndexEntry[] BuildFrameIndexFromAccessUnits(
            EncodedAccessUnitRecord[] accessUnits,
            MetadataTimelineEntryRecord[] metadataEntries)
        {
            var entries = new FrameIndexEntry[accessUnits.Length];
            for (int i = 0; i < accessUnits.Length; i++)
            {
                EncodedAccessUnitRecord accessUnit = accessUnits[i];
                MetadataTimelineEntryRecord metadata = FindMetadataEntry(accessUnit, metadataEntries);
                entries[i] = new FrameIndexEntry
                {
                    frameId = accessUnit.frameId,
                    sourceTimestampUnixMs = accessUnit.sourceTimestampUnixMs,
                    accessUnitId = accessUnit.accessUnitId,
                    encodedPtsUs = accessUnit.encodedPtsUs,
                    mp4SampleIndex = i,
                    metadataTimelineEntryId = metadata.entryId
                };
            }

            return entries;
        }

        private FrameIndexEntry[] BuildFrameIndexFromMetadata(MetadataTimelineEntryRecord[] metadataEntries)
        {
            var entries = new FrameIndexEntry[metadataEntries.Length];
            for (int i = 0; i < metadataEntries.Length; i++)
            {
                entries[i] = ResolveIndexFromMetadata(metadataEntries[i], i);
            }

            return entries;
        }

        private FrameIndexEntry[] BuildFrameIndexFromSnapshots(MergedFrameSnapshotRecord[] synchronizedInputs)
        {
            var entries = new FrameIndexEntry[synchronizedInputs.Length];
            for (int i = 0; i < synchronizedInputs.Length; i++)
            {
                MergedFrameSnapshotRecord input = synchronizedInputs[i];
                long sourceTimestamp = input.cameraTiming.timestampUnixMs > 0
                    ? input.cameraTiming.timestampUnixMs
                    : input.timestampUnixMs;
                entries[i] = new FrameIndexEntry
                {
                    frameId = input.frameId,
                    sourceTimestampUnixMs = sourceTimestamp,
                    accessUnitId = -1,
                    encodedPtsUs = i,
                    mp4SampleIndex = i,
                    metadataTimelineEntryId = -1
                };
            }

            return entries;
        }

        private FrameIndexEntry ResolveIndexFromMetadata(MetadataTimelineEntryRecord metadata)
        {
            return ResolveIndexFromMetadata(metadata, -1);
        }

        private FrameIndexEntry ResolveIndexFromMetadata(MetadataTimelineEntryRecord metadata, int sampleIndex)
        {
            return new FrameIndexEntry
            {
                frameId = metadata.frameId,
                sourceTimestampUnixMs = metadata.sourceTimestampUnixMs > 0
                    ? metadata.sourceTimestampUnixMs
                    : metadata.timestampUnixMs,
                accessUnitId = -1,
                encodedPtsUs = sampleIndex,
                mp4SampleIndex = sampleIndex,
                metadataTimelineEntryId = metadata.entryId
            };
        }

        private MetadataTimelineEntryRecord FindMetadataEntry(
            EncodedAccessUnitRecord accessUnit,
            MetadataTimelineEntryRecord[] metadataEntries)
        {
            for (int i = metadataEntries.Length - 1; i >= 0; i--)
            {
                if (metadataEntries[i].frameId == accessUnit.frameId)
                {
                    return metadataEntries[i];
                }
            }

            return new MetadataTimelineEntryRecord
            {
                entryId = -1,
                frameId = accessUnit.frameId,
                timestampUnixMs = accessUnit.sourceTimestampUnixMs,
                sourceTimestampUnixMs = accessUnit.sourceTimestampUnixMs,
                isSendable = true,
                metadataJson = string.Empty,
                dropReason = string.Empty
            };
        }

        private string ResolveFailureReason(
            EncodedAccessUnitRecord[] accessUnits,
            MergedFrameSnapshotRecord[] synchronizedInputs,
            MetadataTimelineEntryRecord[] metadataEntries,
            FrameIndexEntry[] indexEntries,
            SingleEncodeTimestampSample[] timestampSamples)
        {
            if (encodingHealthState.hasFailure)
            {
                return string.IsNullOrWhiteSpace(encodingHealthState.lastFailureReason)
                    ? "Encoding health reports failure."
                    : encodingHealthState.lastFailureReason;
            }

            if (mp4ArtifactWriterState.hasFailure)
            {
                return string.IsNullOrWhiteSpace(mp4ArtifactWriterState.lastFailureReason)
                    ? "MP4 artifact writer reports failure."
                    : mp4ArtifactWriterState.lastFailureReason;
            }

            if (synchronizedInputs.Length == 0 &&
                metadataEntries.Length == 0 &&
                indexEntries.Length == 0)
            {
                return "No sendable synchronized frames were available from stage 03.";
            }

            if (metadataEntries.Length == 0)
            {
                return "No metadata timeline entries were available from stage 03.";
            }

            if (indexEntries.Length == 0)
            {
                return "No frame index entries could be resolved for stage 05.";
            }

            if (accessUnits.Length == 0 && !mp4ArtifactWriterState.IsUsableArtifact)
            {
                return "No encoded access units or usable MP4 artifact were available from stage 04.";
            }

            if (timestampSamples.Length == 0)
            {
                return "No timestamp samples could be built for stage 05.";
            }

            return string.Empty;
        }

        private int ResolveFrameCount(
            MergedFrameSnapshotRecord[] synchronizedInputs,
            FrameIndexEntry[] indexEntries,
            MetadataTimelineEntryRecord[] metadataEntries,
            EncodedAccessUnitRecord[] accessUnits)
        {
            if (synchronizedInputs.Length > 0)
            {
                return synchronizedInputs.Length;
            }

            if (indexEntries.Length > 0)
            {
                return indexEntries.Length;
            }

            if (metadataEntries.Length > 0)
            {
                return metadataEntries.Length;
            }

            return accessUnits.Length;
        }

        private string ResolveFrameIndexPath()
        {
            string videoPath = mp4ArtifactWriterState.outputPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(videoPath))
            {
                return frameIndexFileName;
            }

            string directory = System.IO.Path.GetDirectoryName(videoPath);
            return string.IsNullOrWhiteSpace(directory)
                ? frameIndexFileName
                : System.IO.Path.Combine(directory, frameIndexFileName);
        }

        private long ResolveFirstFrameId(
            FrameIndexEntry[] indexEntries,
            MetadataTimelineEntryRecord[] metadataEntries,
            MergedFrameSnapshotRecord[] synchronizedInputs)
        {
            if (indexEntries.Length > 0)
            {
                return indexEntries[0].frameId;
            }

            if (metadataEntries.Length > 0)
            {
                return metadataEntries[0].frameId;
            }

            return synchronizedInputs.Length > 0 ? synchronizedInputs[0].frameId : -1;
        }

        private long ResolveLastFrameId(
            FrameIndexEntry[] indexEntries,
            MetadataTimelineEntryRecord[] metadataEntries,
            MergedFrameSnapshotRecord[] synchronizedInputs)
        {
            if (indexEntries.Length > 0)
            {
                return indexEntries[indexEntries.Length - 1].frameId;
            }

            if (metadataEntries.Length > 0)
            {
                return metadataEntries[metadataEntries.Length - 1].frameId;
            }

            return synchronizedInputs.Length > 0 ? synchronizedInputs[synchronizedInputs.Length - 1].frameId : -1;
        }

        private long ResolveTimestampStart(
            FrameIndexEntry[] indexEntries,
            MetadataTimelineEntryRecord[] metadataEntries,
            SingleEncodeTimestampSample[] timestampSamples)
        {
            if (indexEntries.Length > 0)
            {
                return indexEntries[0].sourceTimestampUnixMs;
            }

            if (metadataEntries.Length > 0)
            {
                return metadataEntries[0].sourceTimestampUnixMs > 0
                    ? metadataEntries[0].sourceTimestampUnixMs
                    : metadataEntries[0].timestampUnixMs;
            }

            return timestampSamples.Length > 0 ? timestampSamples[0].sourceTimestampUnixMs : 0;
        }

        private long ResolveTimestampEnd(
            FrameIndexEntry[] indexEntries,
            MetadataTimelineEntryRecord[] metadataEntries,
            SingleEncodeTimestampSample[] timestampSamples)
        {
            if (indexEntries.Length > 0)
            {
                return indexEntries[indexEntries.Length - 1].sourceTimestampUnixMs;
            }

            if (metadataEntries.Length > 0)
            {
                MetadataTimelineEntryRecord last = metadataEntries[metadataEntries.Length - 1];
                return last.sourceTimestampUnixMs > 0 ? last.sourceTimestampUnixMs : last.timestampUnixMs;
            }

            return timestampSamples.Length > 0
                ? timestampSamples[timestampSamples.Length - 1].sourceTimestampUnixMs
                : 0;
        }

        private string ResolveCodec(EncodedAccessUnitRecord[] accessUnits)
        {
            for (int i = accessUnits.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrWhiteSpace(accessUnits[i].codec))
                {
                    return accessUnits[i].codec;
                }
            }

            return string.Empty;
        }

        private int ResolveWidth(EncodedAccessUnitRecord[] accessUnits)
        {
            for (int i = accessUnits.Length - 1; i >= 0; i--)
            {
                if (accessUnits[i].width > 0)
                {
                    return accessUnits[i].width;
                }
            }

            return 0;
        }

        private int ResolveHeight(EncodedAccessUnitRecord[] accessUnits)
        {
            for (int i = accessUnits.Length - 1; i >= 0; i--)
            {
                if (accessUnits[i].height > 0)
                {
                    return accessUnits[i].height;
                }
            }

            return 0;
        }

        private int ResolveFrameRate(SingleEncodeTimestampSample[] timestampSamples)
        {
            if (timestampSamples.Length < 2)
            {
                return 0;
            }

            long durationMs = timestampSamples[timestampSamples.Length - 1].sourceTimestampUnixMs -
                timestampSamples[0].sourceTimestampUnixMs;
            if (durationMs <= 0)
            {
                return 0;
            }

            return Mathf.RoundToInt((timestampSamples.Length - 1) * 1000f / durationMs);
        }

        private long ResolveByteLength(EncodedAccessUnitRecord[] accessUnits)
        {
            if (mp4ArtifactWriterState.byteLength > 0)
            {
                return mp4ArtifactWriterState.byteLength;
            }

            long total = 0;
            for (int i = 0; i < accessUnits.Length; i++)
            {
                total += accessUnits[i].byteLength;
            }

            return total;
        }

        private long ResolveStartUnixMs(long timestampStart)
        {
            if (mp4ArtifactWriterState.startedUnixMs > 0)
            {
                return mp4ArtifactWriterState.startedUnixMs;
            }

            return timestampStart;
        }

        private long ResolveFinalizedUnixMs(long timestampEnd)
        {
            if (mp4ArtifactWriterState.finalizedUnixMs > 0)
            {
                return mp4ArtifactWriterState.finalizedUnixMs;
            }

            return timestampEnd;
        }
    }
}
