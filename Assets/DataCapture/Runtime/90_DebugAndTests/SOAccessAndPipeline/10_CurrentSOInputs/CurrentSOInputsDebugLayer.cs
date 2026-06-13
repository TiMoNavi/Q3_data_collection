using System;
using System.Collections;
using System.Collections.Generic;
using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Testing
{
    [Serializable]
    public sealed class CurrentSOInputsDebugLayer
    {
        // Layer resources:
        // - Required Current SOs: camera image/timing/pose/metadata/stream state and controller pose.
        // - This layer only reads result SOs. It must not write Current SOs to fake frames.
        //
        // Normal:
        // - Every required ICurrentRecordSource is valid and advances beyond its baseline.
        // - CurrentCameraImage.currentTexture is not null.
        // - CurrentCameraStreamState is supported and playing.
        //
        // Advancement:
        // - None. The production capture writers must update Current SOs.

        [SerializeField] private ScriptableObject[] requiredCurrentSources = Array.Empty<ScriptableObject>();
        [SerializeField] private CurrentCameraImageSO currentCameraImage;
        [SerializeField] private CurrentCameraStreamStateSO currentCameraStreamState;
        [SerializeField] private CurrentControllerPoseSO currentControllerPose;
        [SerializeField] private RecordingSessionStateSO recordingState;

        public List<SODebugCurrentBaseline> CaptureBaselines()
        {
            var baselines = new List<SODebugCurrentBaseline>();
            foreach (ScriptableObject asset in requiredCurrentSources)
            {
                if (asset is ICurrentRecordSource source)
                {
                    baselines.Add(new SODebugCurrentBaseline(asset, source.CurrentTimestampUnixMs, source.RecordSequence));
                }
                else
                {
                    baselines.Add(new SODebugCurrentBaseline(asset, 0, long.MinValue));
                }
            }

            return baselines;
        }

        public IEnumerator Run(
            MonoBehaviour owner,
            List<SODebugCurrentBaseline> baselines,
            float timeoutSeconds,
            float pollIntervalSeconds,
            Action<bool> complete)
        {
            float deadline = Time.unscaledTime + Mathf.Max(0.1f, timeoutSeconds);
            while (Time.unscaledTime < deadline)
            {
                if (AreCurrentSourcesHealthy(baselines, out string fields, out string blocker))
                {
                    SODebugLog.Pass(owner, "CurrentSOInputs", fields);
                    complete(true);
                    yield break;
                }

                if (recordingState != null && recordingState.HasException)
                {
                    SODebugLog.Fail(owner, "CurrentSOInputs", "RecordingSessionState", "HasException==false", "HasException=True", recordingState.LastExceptionReason, timeoutSeconds, fields);
                    complete(false);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, pollIntervalSeconds));
            }

            AreCurrentSourcesHealthy(baselines, out string finalFields, out string finalBlocker);
            SODebugLog.Fail(owner, "CurrentSOInputs", "Required Current SOs", "valid and advancing", "not all valid/advancing", finalBlocker, timeoutSeconds, finalFields);
            complete(false);
        }

        private bool AreCurrentSourcesHealthy(List<SODebugCurrentBaseline> baselines, out string fields, out string blocker)
        {
            if (baselines.Count == 0)
            {
                fields = "requiredCurrentSources=0";
                blocker = "No Current SO assets are configured.";
                return false;
            }

            bool allHealthy = true;
            blocker = string.Empty;
            var parts = new List<string>();

            foreach (SODebugCurrentBaseline baseline in baselines)
            {
                if (!(baseline.Asset is ICurrentRecordSource source))
                {
                    allHealthy = false;
                    blocker = SODebugLog.FirstBlocker(blocker, SODebugLog.NameOf(baseline.Asset) + " does not implement ICurrentRecordSource.");
                    parts.Add(SODebugLog.NameOf(baseline.Asset) + ".invalidType=True");
                    continue;
                }

                bool advanced = source.CurrentTimestampUnixMs > baseline.TimestampUnixMs ||
                    source.RecordSequence > baseline.RecordSequence;
                bool healthy = source.IsRecordValid && source.CurrentTimestampUnixMs > 0 && advanced;

                parts.Add(SODebugLog.NameOf(baseline.Asset) +
                    "(valid=" + SODebugLog.Bool(source.IsRecordValid) +
                    ",seq=" + source.RecordSequence +
                    ",baselineSeq=" + baseline.RecordSequence +
                    ",ts=" + source.CurrentTimestampUnixMs +
                    ",baselineTs=" + baseline.TimestampUnixMs + ")");

                if (!healthy)
                {
                    allHealthy = false;
                    blocker = SODebugLog.FirstBlocker(blocker, SODebugLog.NameOf(baseline.Asset) + " is not valid or has not advanced.");
                }
            }

            if (currentCameraImage != null)
            {
                bool textureOk = currentCameraImage.currentTexture != null;
                parts.Add("CurrentCameraImage.texture=" + (textureOk ? currentCameraImage.currentTexture.name : "null"));
                parts.Add("CurrentCameraImage.resolution=" + currentCameraImage.resolution.x + "x" + currentCameraImage.resolution.y);
                if (!textureOk)
                {
                    allHealthy = false;
                    blocker = SODebugLog.FirstBlocker(blocker, "CurrentCameraImage.currentTexture is null.");
                }
            }

            if (currentCameraStreamState != null)
            {
                parts.Add("CurrentCameraStreamState.isPlaying=" + SODebugLog.Bool(currentCameraStreamState.isPlaying));
                parts.Add("CurrentCameraStreamState.isSupported=" + SODebugLog.Bool(currentCameraStreamState.isSupported));
                parts.Add("CurrentCameraStreamState.isUpdatedThisFrame=" + SODebugLog.Bool(currentCameraStreamState.isUpdatedThisFrame));
                parts.Add("CurrentCameraStreamState.measuredFramerate=" + currentCameraStreamState.measuredFramerate.ToString("0.0"));

                if (!currentCameraStreamState.isSupported || !currentCameraStreamState.isPlaying)
                {
                    allHealthy = false;
                    blocker = SODebugLog.FirstBlocker(blocker, "Passthrough camera stream is not supported or not playing.");
                }
            }

            if (currentControllerPose != null)
            {
                parts.Add("CurrentControllerPose.isValid=" + SODebugLog.Bool(currentControllerPose.isValid));
                parts.Add("CurrentControllerPose.timestampUnixMs=" + currentControllerPose.timestampUnixMs);
            }

            fields = string.Join("; ", parts);
            if (string.IsNullOrWhiteSpace(blocker))
            {
                blocker = "All required Current SOs are valid and advancing.";
            }

            return allHealthy;
        }
    }
}
