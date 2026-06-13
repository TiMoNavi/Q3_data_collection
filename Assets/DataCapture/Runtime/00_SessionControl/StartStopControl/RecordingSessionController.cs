using System.Collections.Generic;
using DataCapture.Networking;
using UnityEngine;
using UnityEngine.Events;

namespace SObasic.CurrentQueueBridge
{
    public class RecordingSessionController : MonoBehaviour
    {
        [Header("Preferred State SO")]
        [SerializeField] private RecordingSessionStateSO recordingState;

        [Header("Recording Preconditions")]
        [SerializeField] private OutputRouteGateSO outputRouteGate;
        [SerializeField] private string outputRouteNotReadyReason = "Output route is not ready. Recording is blocked.";

        [Header("Recording State Effects")]
        [SerializeField] private List<ScriptableObject> queuesToClear = new List<ScriptableObject>();
        [SerializeField] private List<CurrentToQueueRecorder> recordersToReset = new List<CurrentToQueueRecorder>();
        [SerializeField] private bool clearQueuesWhenRecordingStops = true;
        [SerializeField] private bool clearQueuesWhenRecordingStarts = true;

        public UnityEvent warmupStarted;
        public UnityEvent recordingStarted;
        public UnityEvent recordingStopped;

        private RecordingSessionState lastState;
        private bool initialized;

        private void OnEnable()
        {
            lastState = recordingState != null ? recordingState.State : RecordingSessionState.NotStarted;
            initialized = true;
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            if (recordingState == null)
            {
                return;
            }

            RecordingSessionState activeState = recordingState.State;
            if (activeState == lastState)
            {
                return;
            }

            lastState = activeState;
            HandleStateChanged(activeState);
        }

        [ContextMenu("Start Recording")]
        public void StartRecording()
        {
            if (!CanStartRecording(out string reason))
            {
                if (recordingState != null)
                {
                    recordingState.StopWithException(reason);
                }

                Debug.LogWarning(reason, this);
                return;
            }

            if (recordingState != null)
            {
                recordingState.BeginWarmup();
            }
        }

        [ContextMenu("Toggle Recording")]
        public void ToggleRecording()
        {
            if (recordingState != null && !recordingState.IsNotStarted)
            {
                StopRecording();
                return;
            }

            StartRecording();
        }

        public bool CanStartRecording(out string reason)
        {
            if (outputRouteGate == null)
            {
                reason = "OutputRouteGateSO is not assigned. " + outputRouteNotReadyReason;
                return false;
            }

            if (!outputRouteGate.CanStartRecording)
            {
                reason = outputRouteGate.GetRecordingBlockReason();
                if (string.IsNullOrWhiteSpace(reason))
                {
                    reason = outputRouteNotReadyReason;
                }
                return false;
            }

            reason = string.Empty;
            return true;
        }

        [ContextMenu("Stop Recording")]
        public void StopRecording()
        {
            if (recordingState != null)
            {
                recordingState.StopRecording();
            }
        }

        [ContextMenu("Abort Recording")]
        public void AbortRecording()
        {
            AbortRecordingWithReason("Recording aborted manually.");
        }

        public void AbortRecordingWithReason(string reason)
        {
            if (recordingState != null)
            {
                recordingState.StopWithException(reason);
            }
        }

        [ContextMenu("Clear Queues")]
        public void ClearQueues()
        {
            foreach (ScriptableObject queueAsset in queuesToClear)
            {
                if (queueAsset is IRecordQueueSink queue)
                {
                    queue.ClearQueue();
                }
            }

            foreach (CurrentToQueueRecorder recorder in recordersToReset)
            {
                if (recorder != null)
                {
                    recorder.ResetRecordGuard();
                }
            }
        }

        private void HandleRecordingStarted()
        {
            if (clearQueuesWhenRecordingStarts)
            {
                ClearQueues();
            }

            recordingStarted?.Invoke();
        }

        private void HandleWarmupStarted()
        {
            if (clearQueuesWhenRecordingStarts)
            {
                ClearQueues();
            }

            warmupStarted?.Invoke();
        }

        private void HandleRecordingStopped()
        {
            if (clearQueuesWhenRecordingStops)
            {
                ClearQueues();
            }

            recordingStopped?.Invoke();
        }

        private void HandleStateChanged(RecordingSessionState state)
        {
            if (state == RecordingSessionState.WarmingUp)
            {
                HandleWarmupStarted();
            }
            else if (state == RecordingSessionState.Recording)
            {
                recordingStarted?.Invoke();
            }
            else
            {
                HandleRecordingStopped();
            }
        }
    }
}
