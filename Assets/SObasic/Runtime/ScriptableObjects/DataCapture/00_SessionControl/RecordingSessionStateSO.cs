using UnityEngine;
using SObasic;

namespace SObasic.CurrentQueueBridge
{
    public enum RecordingSessionState
    {
        NotStarted,
        WarmingUp,
        Recording
    }

    [CreateAssetMenu(fileName = "RecordingSessionStateSO", menuName = "DataCapture/Synchronization/Recording Session State")]
    public class RecordingSessionStateSO : ScriptableObject, IActiveState
    {
        [SerializeField] private RecordingSessionState state = RecordingSessionState.NotStarted;
        [SerializeField] private SObasic.StringVariable exceptionLog;
        [SerializeField] private string lastExceptionReason = string.Empty;
        [SerializeField] private long lastExceptionUnixMs;

        [Header("Runtime Diagnostics")]
        [SerializeField] private bool logStateChangesToUnity = true;
        [SerializeField] private string lastDebugMessage = string.Empty;
        [SerializeField] private long lastStateChangeUnixMs;
        [SerializeField] private int stateChangeCount;
        [SerializeField] private string[] recentDebugEvents = new string[12];

        public RecordingSessionState State => state;
        public bool Active => state != RecordingSessionState.NotStarted;
        public bool IsNotStarted => state == RecordingSessionState.NotStarted;
        public bool IsWarmingUp => state == RecordingSessionState.WarmingUp;
        public bool IsRecording => state == RecordingSessionState.Recording;
        public bool ShouldWriteQueues => state == RecordingSessionState.WarmingUp || state == RecordingSessionState.Recording;
        public string LastExceptionReason => lastExceptionReason;
        public long LastExceptionUnixMs => lastExceptionUnixMs;
        public bool HasException => !string.IsNullOrEmpty(lastExceptionReason);
        public string LastDebugMessage => lastDebugMessage;
        public long LastStateChangeUnixMs => lastStateChangeUnixMs;
        public int StateChangeCount => stateChangeCount;
        public string[] RecentDebugEvents => recentDebugEvents;

        public void BeginWarmup()
        {
            ClearExceptionLog();
            ChangeState(RecordingSessionState.WarmingUp, "Recording warmup started.", false);
        }

        public void StartRecording()
        {
            ChangeState(RecordingSessionState.Recording, "Recording session is active.", false);
        }

        public void StopRecording()
        {
            ChangeState(RecordingSessionState.NotStarted, "Recording stopped normally.", false);
        }

        public void StopWithException(string reason)
        {
            SetExceptionLog(reason);
            ChangeState(RecordingSessionState.NotStarted, lastExceptionReason, true);
        }

        public void Fail(string reason)
        {
            StopWithException(reason);
        }

        public void ResetToNotStarted()
        {
            ChangeState(RecordingSessionState.NotStarted, "Recording state reset to NotStarted.", false);
        }

        public void SetState(RecordingSessionState newState)
        {
            ChangeState(newState, "Recording state set to " + newState + ".", false);
        }

        public void ClearExceptionLog()
        {
            lastExceptionReason = string.Empty;
            lastExceptionUnixMs = 0;
            if (exceptionLog != null)
            {
                exceptionLog.Clear();
            }
        }

        public void SetExceptionLog(string reason)
        {
            lastExceptionReason = string.IsNullOrWhiteSpace(reason)
                ? "Recording stopped by an unspecified exception."
                : reason;
            lastExceptionUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (exceptionLog != null)
            {
                exceptionLog.Value = lastExceptionReason;
            }
        }

        [ContextMenu("Log Runtime Diagnostics")]
        public void LogRuntimeDiagnostics()
        {
            Debug.Log(
                "RecordingSessionState diagnostics: state=" + state +
                " hasException=" + HasException +
                " lastException=" + lastExceptionReason +
                " lastDebug=" + lastDebugMessage,
                this);
        }

        private void ChangeState(RecordingSessionState newState, string reason, bool warning)
        {
            RecordingSessionState previousState = state;
            state = newState;

            if (previousState == newState && !warning)
            {
                return;
            }

            string message = "RecordingSessionState " + previousState + " -> " + newState;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                message += ": " + reason;
            }

            AppendDebugEvent(message);
            if (logStateChangesToUnity)
            {
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

        private void AppendDebugEvent(string message)
        {
            lastDebugMessage = message;
            lastStateChangeUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            stateChangeCount++;

            if (recentDebugEvents == null || recentDebugEvents.Length == 0)
            {
                recentDebugEvents = new string[12];
            }

            for (int i = recentDebugEvents.Length - 1; i > 0; i--)
            {
                recentDebugEvents[i] = recentDebugEvents[i - 1];
            }

            recentDebugEvents[0] = lastStateChangeUnixMs + " " + message;
        }
    }
}
