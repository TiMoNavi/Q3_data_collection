using UnityEngine;

namespace DataCapture.Diagnostics
{
    public enum SoDrivenPipelineStageState
    {
        NotStarted,
        Running,
        Passed,
        Failed,
        Skipped
    }

    [System.Serializable]
    public struct SoDrivenPipelineStageStatus
    {
        public string stageName;
        public SoDrivenPipelineStageState state;
        public bool passed;
        public long checkedAtUnixMs;
        public string statusMessage;
        public string blocker;
        public long frameId;
        public long timestampUnixMs;
        public int count;
        public string codec;
        public int byteLength;

        public void Reset(string name)
        {
            stageName = name;
            state = SoDrivenPipelineStageState.NotStarted;
            passed = false;
            checkedAtUnixMs = 0;
            statusMessage = string.Empty;
            blocker = string.Empty;
            frameId = 0;
            timestampUnixMs = 0;
            count = 0;
            codec = string.Empty;
            byteLength = 0;
        }
    }

    [CreateAssetMenu(fileName = "SoDrivenSixStagePipelineStatusSO", menuName = "DataCapture/90 Diagnostics/SO Driven Six Stage Pipeline Status")]
    public class SoDrivenSixStagePipelineStatusSO : ScriptableObject
    {
        [Header("Overall")]
        public bool isRunning;
        public bool isComplete;
        public bool hasFailure;
        public string activeStage = string.Empty;
        public string lastBlocker = string.Empty;
        public string statusMessage = string.Empty;
        public long startedAtUnixMs;
        public long updatedAtUnixMs;
        public long completedAtUnixMs;
        public int runRevision;

        [Header("Six Stages")]
        public SoDrivenPipelineStageStatus handshakeAndRecording;
        public SoDrivenPipelineStageStatus currentCapture;
        public SoDrivenPipelineStageStatus queueBuffering;
        public SoDrivenPipelineStageStatus synchronization;
        public SoDrivenPipelineStageStatus encoding;
        public SoDrivenPipelineStageStatus networkAndPcReceive;

        [Header("Runtime Diagnostics")]
        public bool logChangesToUnity = true;
        public string[] recentEvents = new string[24];

        [ContextMenu("Reset Pipeline Status")]
        public void ResetState()
        {
            isRunning = false;
            isComplete = false;
            hasFailure = false;
            activeStage = string.Empty;
            lastBlocker = string.Empty;
            statusMessage = string.Empty;
            startedAtUnixMs = 0;
            updatedAtUnixMs = 0;
            completedAtUnixMs = 0;

            handshakeAndRecording.Reset("0-1 Network handshake + recording gate");
            currentCapture.Reset("2 Current SO capture");
            queueBuffering.Reset("3 Queue buffering");
            synchronization.Reset("4 Timestamp synchronization");
            encoding.Reset("5 Encoding output");
            networkAndPcReceive.Reset("6 Network send + PC receive");

            ClearRecentEvents();
        }

        public void BeginRun(string message)
        {
            ResetState();
            isRunning = true;
            runRevision++;
            startedAtUnixMs = Now();
            updatedAtUnixMs = startedAtUnixMs;
            statusMessage = message ?? string.Empty;
            AppendEvent(statusMessage, false);
        }

        public void MarkStage(
            string stageKey,
            SoDrivenPipelineStageState state,
            string message,
            string blocker = "",
            long frameId = 0,
            long timestampUnixMs = 0,
            int count = 0,
            string codec = "",
            int byteLength = 0)
        {
            SoDrivenPipelineStageStatus status = GetStage(stageKey);
            status.state = state;
            status.passed = state == SoDrivenPipelineStageState.Passed;
            status.checkedAtUnixMs = Now();
            status.statusMessage = message ?? string.Empty;
            status.blocker = blocker ?? string.Empty;
            status.frameId = frameId;
            status.timestampUnixMs = timestampUnixMs;
            status.count = count;
            status.codec = codec ?? string.Empty;
            status.byteLength = byteLength;
            SetStage(stageKey, status);

            activeStage = status.stageName;
            updatedAtUnixMs = status.checkedAtUnixMs;
            statusMessage = status.statusMessage;
            lastBlocker = status.blocker;

            if (state == SoDrivenPipelineStageState.Failed)
            {
                hasFailure = true;
            }

            AppendEvent(status.stageName + " " + state + ": " + status.statusMessage, state == SoDrivenPipelineStageState.Failed);
        }

        public void Complete(string message)
        {
            isRunning = false;
            isComplete = true;
            hasFailure = false;
            completedAtUnixMs = Now();
            updatedAtUnixMs = completedAtUnixMs;
            statusMessage = message ?? string.Empty;
            lastBlocker = string.Empty;
            AppendEvent(statusMessage, false);
        }

        public void Fail(string message)
        {
            isRunning = false;
            isComplete = false;
            hasFailure = true;
            completedAtUnixMs = Now();
            updatedAtUnixMs = completedAtUnixMs;
            statusMessage = message ?? string.Empty;
            lastBlocker = statusMessage;
            AppendEvent(statusMessage, true);
        }

        private SoDrivenPipelineStageStatus GetStage(string stageKey)
        {
            switch (NormalizeStageKey(stageKey))
            {
                case "handshake":
                    return handshakeAndRecording;
                case "capture":
                    return currentCapture;
                case "queue":
                    return queueBuffering;
                case "sync":
                    return synchronization;
                case "encoding":
                    return encoding;
                case "network":
                    return networkAndPcReceive;
                default:
                    return handshakeAndRecording;
            }
        }

        private void SetStage(string stageKey, SoDrivenPipelineStageStatus status)
        {
            switch (NormalizeStageKey(stageKey))
            {
                case "handshake":
                    handshakeAndRecording = status;
                    break;
                case "capture":
                    currentCapture = status;
                    break;
                case "queue":
                    queueBuffering = status;
                    break;
                case "sync":
                    synchronization = status;
                    break;
                case "encoding":
                    encoding = status;
                    break;
                case "network":
                    networkAndPcReceive = status;
                    break;
            }
        }

        private static string NormalizeStageKey(string stageKey)
        {
            if (string.IsNullOrWhiteSpace(stageKey))
            {
                return "handshake";
            }

            string key = stageKey.Trim().ToLowerInvariant();
            if (key == "recording" || key == "pc")
            {
                return "handshake";
            }

            if (key == "current")
            {
                return "capture";
            }

            if (key == "synchronization" || key == "merge" || key == "merger")
            {
                return "sync";
            }

            if (key == "send" || key == "receiver" || key == "pc_receive")
            {
                return "network";
            }

            return key;
        }

        private void AppendEvent(string message, bool warning)
        {
            if (recentEvents == null || recentEvents.Length == 0)
            {
                recentEvents = new string[24];
            }

            for (int i = recentEvents.Length - 1; i > 0; i--)
            {
                recentEvents[i] = recentEvents[i - 1];
            }

            recentEvents[0] = Now() + " " + (message ?? string.Empty);

            if (!logChangesToUnity)
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

        private void ClearRecentEvents()
        {
            if (recentEvents == null || recentEvents.Length == 0)
            {
                recentEvents = new string[24];
                return;
            }

            for (int i = 0; i < recentEvents.Length; i++)
            {
                recentEvents[i] = string.Empty;
            }
        }

        private static long Now()
        {
            return System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
