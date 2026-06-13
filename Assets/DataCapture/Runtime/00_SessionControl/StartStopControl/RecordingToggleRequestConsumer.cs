using UnityEngine;

namespace SObasic.CurrentQueueBridge
{
    public class RecordingToggleRequestConsumer : MonoBehaviour
    {
        [SerializeField] private RecordingToggleRequestSO toggleRequest;
        [SerializeField] private RecordingSessionController recordingSessionController;
        [SerializeField] private bool consumeRequestAfterHandling = true;

        private int lastHandledRevision;
        private bool lastRequestedState;

        private void OnEnable()
        {
            if (toggleRequest != null)
            {
                lastHandledRevision = toggleRequest.requestRevision;
            }

            lastRequestedState = false;
        }

        private void Reset()
        {
            recordingSessionController = GetComponent<RecordingSessionController>();
        }

        private void Awake()
        {
            if (recordingSessionController == null)
            {
                recordingSessionController = GetComponent<RecordingSessionController>();
            }
        }

        private void Update()
        {
            if (toggleRequest == null || recordingSessionController == null)
            {
                return;
            }

            bool revisionChanged = toggleRequest.requestRevision != lastHandledRevision;
            bool manualBoolRaised = toggleRequest.requested && !lastRequestedState;
            lastRequestedState = toggleRequest.requested;

            if (!revisionChanged && !manualBoolRaised)
            {
                return;
            }

            lastHandledRevision = toggleRequest.requestRevision;
            recordingSessionController.ToggleRecording();

            if (consumeRequestAfterHandling)
            {
                toggleRequest.Clear();
                lastRequestedState = false;
            }
        }
    }
}
