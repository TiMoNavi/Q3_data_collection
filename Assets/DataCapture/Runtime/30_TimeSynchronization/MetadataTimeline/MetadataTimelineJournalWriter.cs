using System;
using UnityEngine;

namespace DataCapture.Synchronization
{
    public class MetadataTimelineJournalWriter : MonoBehaviour
    {
        [SerializeField] private MergedFrameSnapshotQueueSO mergedQueue;
        [SerializeField] private MetadataTimelineJournalSO journal;
        [SerializeField] private bool updateEveryFrame = true;
        [SerializeField] private bool recordOnlySendableSnapshots;

        private long lastRecordedFrameId = -1;
        private long lastRecordedTimestampUnixMs = -1;
        private long lastObservedQueueGenerationId = -1;
        private long nextEntryId;

        private void Update()
        {
            if (updateEveryFrame)
            {
                RecordLatestSnapshot();
            }
        }

        [ContextMenu("Record Latest Snapshot")]
        public bool RecordLatestSnapshot()
        {
            if (mergedQueue == null || journal == null)
            {
                return false;
            }

            if (lastObservedQueueGenerationId != mergedQueue.GenerationId)
            {
                lastObservedQueueGenerationId = mergedQueue.GenerationId;
                lastRecordedFrameId = -1;
                lastRecordedTimestampUnixMs = -1;
            }

            MergedFrameSnapshotRecord[] records = mergedQueue.ExportSnapshot();
            if (records.Length == 0)
            {
                return false;
            }

            MergedFrameSnapshotRecord latest = records[records.Length - 1];
            if (recordOnlySendableSnapshots && !latest.isSendable)
            {
                return false;
            }

            if (latest.frameId == lastRecordedFrameId &&
                latest.timestampUnixMs == lastRecordedTimestampUnixMs)
            {
                return false;
            }

            var entry = new MetadataTimelineEntryRecord
            {
                entryId = nextEntryId++,
                frameId = latest.frameId,
                timestampUnixMs = latest.timestampUnixMs,
                sourceTimestampUnixMs = latest.cameraTiming.timestampUnixMs > 0
                    ? latest.cameraTiming.timestampUnixMs
                    : latest.timestampUnixMs,
                isSendable = latest.isSendable,
                metadataJson = JsonUtility.ToJson(latest),
                dropReason = latest.dropReason ?? string.Empty
            };

            journal.RecordData(entry);
            lastRecordedFrameId = latest.frameId;
            lastRecordedTimestampUnixMs = latest.timestampUnixMs;
            return true;
        }

        [ContextMenu("Reset Writer State")]
        public void ResetWriterState()
        {
            lastRecordedFrameId = -1;
            lastRecordedTimestampUnixMs = -1;
            lastObservedQueueGenerationId = mergedQueue != null ? mergedQueue.GenerationId : -1;
            nextEntryId = journal != null ? journal.Count : 0;
        }

        private void OnEnable()
        {
            ResetWriterState();
        }
    }
}
