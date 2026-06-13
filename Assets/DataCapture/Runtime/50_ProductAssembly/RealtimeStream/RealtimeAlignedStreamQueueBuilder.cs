using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Networking
{
    public sealed class RealtimeAlignedStreamQueueBuilder : MonoBehaviour
    {
        [Header("Inputs")]
        [SerializeField] private EncodedAccessUnitQueueSO encodedAccessUnitQueue;
        [SerializeField] private MetadataTimelineJournalSO metadataTimelineJournal;
        [SerializeField] private NetworkSenderConfigurationSO networkConfiguration;
        [SerializeField] private SessionModeSO sessionMode;

        [Header("Output")]
        [SerializeField] private RealtimeAlignedStreamQueueSO realtimeAlignedStreamQueue;

        [Header("Runtime")]
        [SerializeField] private bool buildOnUpdate = true;
        [SerializeField] private bool requireNetworkMode = true;
        [SerializeField] private bool requireSendableMetadata = true;
        [SerializeField] private long timestampToleranceMs = 10;

        [Header("Diagnostics")]
        [SerializeField] private long lastConsumedAccessUnitId = -1;
        [SerializeField] private long nextRecordId;
        [SerializeField] private int builtRecordCount;
        [SerializeField] private int skippedRecordCount;
        [SerializeField] private string lastStatus;

        private void Update()
        {
            if (buildOnUpdate)
            {
                BuildAvailableRecords();
            }
        }

        [ContextMenu("Build Available Realtime Records")]
        public int BuildAvailableRecords()
        {
            if (!Validate(out string blocker))
            {
                lastStatus = blocker;
                return 0;
            }

            if (requireNetworkMode && !UsesNetworkOutput())
            {
                lastStatus = "Realtime stream is idle because the current output target does not require network streaming.";
                return 0;
            }

            EncodedAccessUnitRecord[] accessUnits = encodedAccessUnitQueue.ExportSnapshot();
            int builtThisPass = 0;

            for (int i = 0; i < accessUnits.Length; i++)
            {
                EncodedAccessUnitRecord accessUnit = accessUnits[i];
                if (accessUnit.accessUnitId <= lastConsumedAccessUnitId)
                {
                    continue;
                }

                if (!TryBuildRecord(accessUnit, out RealtimeAlignedStreamRecord streamRecord))
                {
                    skippedRecordCount++;
                    lastConsumedAccessUnitId = accessUnit.accessUnitId;
                    realtimeAlignedStreamQueue.RecordData(streamRecord);
                    continue;
                }

                realtimeAlignedStreamQueue.RecordData(streamRecord);
                lastConsumedAccessUnitId = accessUnit.accessUnitId;
                builtRecordCount++;
                builtThisPass++;
            }

            lastStatus = builtThisPass > 0
                ? "Built " + builtThisPass + " realtime aligned stream record(s)."
                : "No new encoded access units were available.";
            return builtThisPass;
        }

        private bool TryBuildRecord(
            EncodedAccessUnitRecord accessUnit,
            out RealtimeAlignedStreamRecord streamRecord)
        {
            MetadataTimelineEntryRecord metadataEntry = FindMetadataEntry(accessUnit);
            string dropReason = ResolveDropReason(accessUnit, metadataEntry);

            streamRecord = new RealtimeAlignedStreamRecord
            {
                recordId = nextRecordId++,
                frameId = accessUnit.frameId,
                timestampUnixMs = ResolveTimestamp(accessUnit, metadataEntry),
                accessUnitId = accessUnit.accessUnitId,
                metadataTimelineEntryId = metadataEntry.entryId,
                accessUnit = accessUnit,
                metadataEntry = metadataEntry,
                dropReason = dropReason
            };

            return string.IsNullOrWhiteSpace(dropReason);
        }

        private MetadataTimelineEntryRecord FindMetadataEntry(EncodedAccessUnitRecord accessUnit)
        {
            if (metadataTimelineJournal == null)
            {
                return default;
            }

            MetadataTimelineEntryRecord[] entries = metadataTimelineJournal.ExportSnapshot();
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                if (entries[i].frameId == accessUnit.frameId)
                {
                    return entries[i];
                }
            }

            long timestamp = accessUnit.sourceTimestampUnixMs != 0
                ? accessUnit.sourceTimestampUnixMs
                : accessUnit.timestampUnixMs;
            long tolerance = timestampToleranceMs < 0 ? 0 : timestampToleranceMs;
            return metadataTimelineJournal.GetDataAt(timestamp, tolerance);
        }

        private string ResolveDropReason(
            EncodedAccessUnitRecord accessUnit,
            MetadataTimelineEntryRecord metadataEntry)
        {
            if (accessUnit.accessUnitId < 0)
            {
                return "Encoded access unit id is invalid.";
            }

            if (accessUnit.byteLength <= 0 &&
                accessUnit.sampleBytes == null &&
                string.IsNullOrWhiteSpace(accessUnit.sampleFilePath))
            {
                return "Encoded access unit has no payload reference.";
            }

            if (metadataEntry.timestampUnixMs <= 0)
            {
                return "No metadata timeline entry matched the encoded access unit.";
            }

            if (requireSendableMetadata && !metadataEntry.isSendable)
            {
                return string.IsNullOrWhiteSpace(metadataEntry.dropReason)
                    ? "Matched metadata entry is not sendable."
                    : metadataEntry.dropReason;
            }

            return string.Empty;
        }

        private long ResolveTimestamp(
            EncodedAccessUnitRecord accessUnit,
            MetadataTimelineEntryRecord metadataEntry)
        {
            if (metadataEntry.timestampUnixMs > 0)
            {
                return metadataEntry.timestampUnixMs;
            }

            if (accessUnit.sourceTimestampUnixMs > 0)
            {
                return accessUnit.sourceTimestampUnixMs;
            }

            return accessUnit.timestampUnixMs;
        }

        private bool UsesNetworkOutput()
        {
            if (networkConfiguration != null)
            {
                return networkConfiguration.UsesNetwork;
            }

            return sessionMode == null || sessionMode.UsesNetwork;
        }

        private bool Validate(out string blocker)
        {
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

            if (realtimeAlignedStreamQueue == null)
            {
                blocker = "RealtimeAlignedStreamQueueSO is not assigned.";
                return false;
            }

            blocker = string.Empty;
            return true;
        }
    }
}
