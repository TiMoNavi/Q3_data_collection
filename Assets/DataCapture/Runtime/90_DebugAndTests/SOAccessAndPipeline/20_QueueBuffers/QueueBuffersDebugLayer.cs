using System;
using System.Collections;
using System.Collections.Generic;
using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Testing
{
    [Serializable]
    public sealed class QueueBuffersDebugLayer
    {
        // Layer resources:
        // - Required queue SOs matching the Current SO inputs.
        // - RecordingSessionStateSO is read only to confirm ShouldWriteQueues.
        //
        // Normal:
        // - RecordingSessionStateSO.ShouldWriteQueues == true.
        // - Each required IQueueHealth Count and NewestTimestamp grows beyond baseline.
        //
        // Advancement:
        // - None. CurrentToQueueRecorder must write queues from real Current SOs.

        [SerializeField] private ScriptableObject[] requiredQueues = Array.Empty<ScriptableObject>();
        [SerializeField] private RecordingSessionStateSO recordingState;

        public List<SODebugQueueBaseline> CaptureBaselines()
        {
            var baselines = new List<SODebugQueueBaseline>();
            foreach (ScriptableObject asset in requiredQueues)
            {
                if (asset is IQueueHealth queue)
                {
                    baselines.Add(new SODebugQueueBaseline(asset, queue.Count, queue.NewestTimestamp, queue.GenerationId));
                }
                else
                {
                    baselines.Add(new SODebugQueueBaseline(asset, 0, 0, 0));
                }
            }

            return baselines;
        }

        public IEnumerator Run(
            MonoBehaviour owner,
            List<SODebugQueueBaseline> baselines,
            float timeoutSeconds,
            float pollIntervalSeconds,
            Action<bool> complete)
        {
            float deadline = Time.unscaledTime + Mathf.Max(0.1f, timeoutSeconds);
            while (Time.unscaledTime < deadline)
            {
                if (AreQueuesHealthy(baselines, out string fields, out string blocker))
                {
                    SODebugLog.Pass(owner, "QueueBuffers", fields);
                    complete(true);
                    yield break;
                }

                if (recordingState != null && recordingState.HasException)
                {
                    SODebugLog.Fail(owner, "QueueBuffers", "RecordingSessionState", "HasException==false", "HasException=True", recordingState.LastExceptionReason, timeoutSeconds, BuildRecordingFields());
                    complete(false);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, pollIntervalSeconds));
            }

            AreQueuesHealthy(baselines, out string finalFields, out string finalBlocker);
            SODebugLog.Fail(owner, "QueueBuffers", "Required Queue SOs", "Count/NewestTimestamp grow", "not all queues grew", finalBlocker, timeoutSeconds, finalFields);
            complete(false);
        }

        private bool AreQueuesHealthy(List<SODebugQueueBaseline> baselines, out string fields, out string blocker)
        {
            if (baselines.Count == 0)
            {
                fields = "requiredQueues=0";
                blocker = "No required Queue SO assets are configured.";
                return false;
            }

            if (recordingState == null || !recordingState.ShouldWriteQueues)
            {
                fields = BuildRecordingFields();
                blocker = "RecordingSessionState.ShouldWriteQueues is false.";
                return false;
            }

            bool allHealthy = true;
            blocker = string.Empty;
            var parts = new List<string>();

            foreach (SODebugQueueBaseline baseline in baselines)
            {
                if (!(baseline.Asset is IQueueHealth queue))
                {
                    allHealthy = false;
                    blocker = SODebugLog.FirstBlocker(blocker, SODebugLog.NameOf(baseline.Asset) + " does not implement IQueueHealth.");
                    parts.Add(SODebugLog.NameOf(baseline.Asset) + ".invalidType=True");
                    continue;
                }

                bool countGrew = queue.Count > baseline.Count;
                bool timestampGrew = queue.NewestTimestamp > baseline.NewestTimestamp;
                bool healthy = countGrew && timestampGrew;

                parts.Add(SODebugLog.NameOf(baseline.Asset) +
                    "(count=" + queue.Count +
                    ",baselineCount=" + baseline.Count +
                    ",capacity=" + queue.Capacity +
                    ",newest=" + queue.NewestTimestamp +
                    ",baselineNewest=" + baseline.NewestTimestamp +
                    ",overwrite=" + queue.OverwriteCount +
                    ",generation=" + queue.GenerationId + ")");

                if (!healthy)
                {
                    allHealthy = false;
                    blocker = SODebugLog.FirstBlocker(blocker, SODebugLog.NameOf(baseline.Asset) + " Count/NewestTimestamp did not grow.");
                }
            }

            fields = string.Join("; ", parts);
            if (string.IsNullOrWhiteSpace(blocker))
            {
                blocker = "All required queues grew.";
            }

            return allHealthy;
        }

        private string BuildRecordingFields()
        {
            return SODebugLog.Fields(
                "State=" + (recordingState != null ? recordingState.State.ToString() : "null"),
                "ShouldWriteQueues=" + (recordingState != null ? SODebugLog.Bool(recordingState.ShouldWriteQueues) : "null"),
                "HasException=" + (recordingState != null ? SODebugLog.Bool(recordingState.HasException) : "null"),
                "LastExceptionReason=" + SODebugLog.Empty(recordingState != null ? recordingState.LastExceptionReason : null));
        }
    }
}
