using System.Collections.Generic;
using System.IO;
using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Networking
{
    public sealed class FrameIndexWriter : MonoBehaviour
    {
        [Header("Inputs")]
        [SerializeField] private EncodedAccessUnitQueueSO encodedAccessUnitQueue;
        [SerializeField] private MetadataTimelineJournalSO metadataTimelineJournal;
        [SerializeField] private Mp4ArtifactWriterStateSO mp4ArtifactWriterState;

        [Header("Output")]
        [SerializeField] private FrameIndexSO frameIndex;

        [Header("Runtime")]
        [SerializeField] private bool buildOnUpdate = true;
        [SerializeField] private bool rebuildFromMp4SidecarWhenFinalized = true;
        [SerializeField] private bool requireSendableMetadata = true;
        [SerializeField] private long timestampToleranceMs = 10;

        [Header("Diagnostics")]
        [SerializeField] private long lastConsumedAccessUnitId = -1;
        [SerializeField] private string lastIndexedMp4Path;
        [SerializeField] private int indexedAccessUnitCount;
        [SerializeField] private int indexedMp4SampleCount;
        [SerializeField] private int skippedCount;
        [SerializeField] private string lastStatus;

        private void Update()
        {
            if (buildOnUpdate)
            {
                BuildAvailableIndexEntries();
            }
        }

        [ContextMenu("Build Available Frame Index Entries")]
        public int BuildAvailableIndexEntries()
        {
            if (!Validate(out string blocker))
            {
                lastStatus = blocker;
                return 0;
            }

            int built = BuildFromAccessUnits();
            if (built > 0)
            {
                lastStatus = "Indexed " + built + " encoded access unit(s).";
                return built;
            }

            if (rebuildFromMp4SidecarWhenFinalized && TryBuildFromMp4Sidecar(out int sidecarCount))
            {
                lastStatus = "Indexed " + sidecarCount + " MP4 sample(s) from metadata sidecar.";
                return sidecarCount;
            }

            lastStatus = "No new frame index entries were available.";
            return 0;
        }

        [ContextMenu("Clear Frame Index")]
        public void ClearIndex()
        {
            frameIndex?.Clear();
            lastConsumedAccessUnitId = -1;
            lastIndexedMp4Path = string.Empty;
            indexedAccessUnitCount = 0;
            indexedMp4SampleCount = 0;
            skippedCount = 0;
            lastStatus = "Frame index cleared.";
        }

        private int BuildFromAccessUnits()
        {
            if (encodedAccessUnitQueue == null || encodedAccessUnitQueue.Count == 0)
            {
                return 0;
            }

            EncodedAccessUnitRecord[] accessUnits = encodedAccessUnitQueue.ExportSnapshot();
            int built = 0;
            for (int i = 0; i < accessUnits.Length; i++)
            {
                EncodedAccessUnitRecord accessUnit = accessUnits[i];
                if (accessUnit.accessUnitId <= lastConsumedAccessUnitId)
                {
                    continue;
                }

                if (!TryBuildAccessUnitEntry(accessUnit, out FrameIndexEntry entry))
                {
                    skippedCount++;
                    lastConsumedAccessUnitId = accessUnit.accessUnitId;
                    continue;
                }

                frameIndex.Append(entry);
                lastConsumedAccessUnitId = accessUnit.accessUnitId;
                indexedAccessUnitCount++;
                built++;
            }

            return built;
        }

        private bool TryBuildAccessUnitEntry(EncodedAccessUnitRecord accessUnit, out FrameIndexEntry entry)
        {
            entry = default;
            if (accessUnit.accessUnitId < 0)
            {
                return false;
            }

            MetadataTimelineEntryRecord metadataEntry = FindMetadataEntry(accessUnit.frameId, accessUnit.sourceTimestampUnixMs);
            if (!IsUsableMetadata(metadataEntry))
            {
                return false;
            }

            entry = new FrameIndexEntry
            {
                frameId = accessUnit.frameId,
                sourceTimestampUnixMs = ResolveSourceTimestamp(accessUnit, metadataEntry),
                accessUnitId = accessUnit.accessUnitId,
                encodedPtsUs = accessUnit.encodedPtsUs,
                mp4SampleIndex = ResolveNextMp4SampleIndex(),
                metadataTimelineEntryId = metadataEntry.entryId
            };
            return true;
        }

        private bool TryBuildFromMp4Sidecar(out int built)
        {
            built = 0;
            if (indexedAccessUnitCount > 0 || HasAccessUnitIndexEntries())
            {
                return false;
            }

            if (mp4ArtifactWriterState == null ||
                !mp4ArtifactWriterState.IsUsableArtifact ||
                string.IsNullOrWhiteSpace(mp4ArtifactWriterState.metadataSidecarPath) ||
                !File.Exists(mp4ArtifactWriterState.metadataSidecarPath))
            {
                return false;
            }

            string mp4Path = mp4ArtifactWriterState.outputPath ?? string.Empty;
            if (mp4Path == lastIndexedMp4Path)
            {
                return false;
            }

            var entries = new List<FrameIndexEntry>();
            using (var reader = new StreamReader(mp4ArtifactWriterState.metadataSidecarPath))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (!TryBuildSidecarEntry(line, entries.Count, out FrameIndexEntry entry))
                    {
                        skippedCount++;
                        continue;
                    }

                    entries.Add(entry);
                }
            }

            if (entries.Count == 0)
            {
                return false;
            }

            frameIndex.SetEntries(entries.ToArray());
            lastIndexedMp4Path = mp4Path;
            indexedMp4SampleCount = entries.Count;
            built = entries.Count;
            return true;
        }

        private bool HasAccessUnitIndexEntries()
        {
            if (frameIndex == null || frameIndex.Entries == null)
            {
                return false;
            }

            FrameIndexEntry[] entries = frameIndex.Entries;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].accessUnitId >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryBuildSidecarEntry(string metadataJson, int sampleIndex, out FrameIndexEntry entry)
        {
            entry = default;
            MergedFrameSnapshotRecord snapshot;
            try
            {
                snapshot = JsonUtility.FromJson<MergedFrameSnapshotRecord>(metadataJson);
            }
            catch
            {
                return false;
            }

            if (snapshot.timestampUnixMs <= 0 && snapshot.cameraTiming.timestampUnixMs <= 0)
            {
                return false;
            }

            MetadataTimelineEntryRecord metadataEntry = FindMetadataEntry(snapshot.frameId, ResolveSnapshotTimestamp(snapshot));
            long metadataEntryId = metadataEntry.entryId >= 0 ? metadataEntry.entryId : -1;
            entry = new FrameIndexEntry
            {
                frameId = snapshot.frameId,
                sourceTimestampUnixMs = ResolveSnapshotTimestamp(snapshot),
                accessUnitId = -1,
                encodedPtsUs = sampleIndex,
                mp4SampleIndex = sampleIndex,
                metadataTimelineEntryId = metadataEntryId
            };
            return true;
        }

        private MetadataTimelineEntryRecord FindMetadataEntry(long frameId, long timestampUnixMs)
        {
            if (metadataTimelineJournal == null)
            {
                return default;
            }

            MetadataTimelineEntryRecord[] entries = metadataTimelineJournal.ExportSnapshot();
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                if (entries[i].frameId == frameId)
                {
                    return entries[i];
                }
            }

            long tolerance = timestampToleranceMs < 0 ? 0 : timestampToleranceMs;
            return metadataTimelineJournal.GetDataAt(timestampUnixMs, tolerance);
        }

        private bool IsUsableMetadata(MetadataTimelineEntryRecord metadataEntry)
        {
            if (metadataEntry.timestampUnixMs <= 0 && metadataEntry.sourceTimestampUnixMs <= 0)
            {
                return false;
            }

            return !requireSendableMetadata || metadataEntry.isSendable;
        }

        private int ResolveNextMp4SampleIndex()
        {
            return frameIndex != null ? frameIndex.Count : 0;
        }

        private static long ResolveSourceTimestamp(
            EncodedAccessUnitRecord accessUnit,
            MetadataTimelineEntryRecord metadataEntry)
        {
            if (metadataEntry.sourceTimestampUnixMs > 0)
            {
                return metadataEntry.sourceTimestampUnixMs;
            }

            if (accessUnit.sourceTimestampUnixMs > 0)
            {
                return accessUnit.sourceTimestampUnixMs;
            }

            return accessUnit.timestampUnixMs;
        }

        private static long ResolveSnapshotTimestamp(MergedFrameSnapshotRecord snapshot)
        {
            return snapshot.cameraTiming.timestampUnixMs > 0
                ? snapshot.cameraTiming.timestampUnixMs
                : snapshot.timestampUnixMs;
        }

        private bool Validate(out string blocker)
        {
            if (frameIndex == null)
            {
                blocker = "FrameIndexSO is not assigned.";
                return false;
            }

            if (metadataTimelineJournal == null)
            {
                blocker = "MetadataTimelineJournalSO is not assigned.";
                return false;
            }

            if (encodedAccessUnitQueue == null && mp4ArtifactWriterState == null)
            {
                blocker = "No encoded access unit queue or MP4 artifact writer state is assigned.";
                return false;
            }

            blocker = string.Empty;
            return true;
        }
    }
}
