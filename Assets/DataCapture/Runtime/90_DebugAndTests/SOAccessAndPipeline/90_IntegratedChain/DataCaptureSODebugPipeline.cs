using System.Collections;
using SObasic;
using UnityEngine;

namespace DataCapture.Testing
{
    public sealed class DataCaptureSODebugPipeline : MonoBehaviour
    {
        [Header("Run Control")]
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private bool normalizeRecordingStateBeforeRun = true;
        [SerializeField] private bool stopRecordingAtEnd = true;
        [SerializeField] private float startDelaySeconds = 1.5f;
        [SerializeField] private float recordingDurationSeconds = 8f;
        [SerializeField] private float pollIntervalSeconds = 0.25f;

        [Header("Timeouts")]
        [SerializeField] private float handshakeTimeoutSeconds = 8f;
        [SerializeField] private float recordingStartTimeoutSeconds = 3f;
        [SerializeField] private float currentInputsTimeoutSeconds = 6f;
        [SerializeField] private float queueTimeoutSeconds = 6f;
        [SerializeField] private float synchronizationTimeoutSeconds = 6f;
        [SerializeField] private float debugImageEncodingTimeoutSeconds = 8f;
        [SerializeField] private float debugImageNetworkTimeoutSeconds = 4f;
        [SerializeField] private float recordingStopTimeoutSeconds = 3f;

        [Header("Runtime State Reset")]
        [SerializeField] private bool resetRuntimeStateBeforeRun = true;
        [SerializeField] private SORuntimeStateResetter runtimeStateResetter;

        [Header("Layer Debuggers")]
        [SerializeField] private HandshakeRecordingControlDebugLayer networkLayer = new HandshakeRecordingControlDebugLayer();
        [SerializeField] private CurrentSOInputsDebugLayer currentLayer = new CurrentSOInputsDebugLayer();
        [SerializeField] private QueueBuffersDebugLayer queueLayer = new QueueBuffersDebugLayer();
        [SerializeField] private SynchronizationDebugLayer synchronizationLayer = new SynchronizationDebugLayer();
        [SerializeField] private EncodingDecodeDebugLayer encodingLayer = new EncodingDecodeDebugLayer();
        [SerializeField] private NetworkSendDebugLayer networkSendLayer = new NetworkSendDebugLayer();

        private Coroutine activeRun;

        private void Start()
        {
            if (runOnStart)
            {
                activeRun = StartCoroutine(RunPipeline());
            }
        }

        private void OnDisable()
        {
            if (activeRun == null)
            {
                return;
            }

            StopCoroutine(activeRun);
            activeRun = null;
        }

        [ContextMenu("Run SO Debug Pipeline")]
        public void RunFromContextMenu()
        {
            if (activeRun != null)
            {
                StopCoroutine(activeRun);
            }

            activeRun = StartCoroutine(RunPipeline());
        }

        private IEnumerator RunPipeline()
        {
            bool passed = false;
            networkLayer.ResetRunState();

            if (resetRuntimeStateBeforeRun)
            {
                ResetRuntimeStateBeforeRun();
            }

            SODebugLog.Pass(this, "Start", SODebugLog.Fields(
                "startDelaySeconds=" + startDelaySeconds,
                "recordingDurationSeconds=" + recordingDurationSeconds,
                "encodingMode=DebugImageOnly",
                "networkSendChecked=DebugImagePacketOnly"));

            if (startDelaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(startDelaySeconds);
            }

            if (normalizeRecordingStateBeforeRun)
            {
                yield return networkLayer.NormalizeRecordingBeforeRun(this, recordingStopTimeoutSeconds, pollIntervalSeconds, result => passed = result);
                if (!passed)
                {
                    activeRun = null;
                    yield break;
                }
            }

            yield return networkLayer.RunHandshake(this, handshakeTimeoutSeconds, pollIntervalSeconds, result => passed = result);
            if (!passed)
            {
                yield return networkLayer.StopRecordingWindowIfNeeded(this, stopRecordingAtEnd, recordingDurationSeconds, recordingStopTimeoutSeconds, pollIntervalSeconds);
                activeRun = null;
                yield break;
            }

            yield return networkLayer.RunRecordingStart(this, recordingStartTimeoutSeconds, result => passed = result);
            if (!passed)
            {
                yield return networkLayer.StopRecordingWindowIfNeeded(this, stopRecordingAtEnd, recordingDurationSeconds, recordingStopTimeoutSeconds, pollIntervalSeconds);
                activeRun = null;
                yield break;
            }

            var currentBaselines = currentLayer.CaptureBaselines();
            var queueBaselines = queueLayer.CaptureBaselines();
            var syncBaseline = synchronizationLayer.CaptureBaseline();

            yield return currentLayer.Run(this, currentBaselines, currentInputsTimeoutSeconds, pollIntervalSeconds, result => passed = result);
            if (passed)
            {
                yield return queueLayer.Run(this, queueBaselines, queueTimeoutSeconds, pollIntervalSeconds, result => passed = result);
            }

            if (passed)
            {
                yield return synchronizationLayer.Run(this, syncBaseline, synchronizationTimeoutSeconds, pollIntervalSeconds, result => passed = result);
            }

            if (passed)
            {
                SODebugDebugImageObservation debugImage = SODebugDebugImageObservation.Empty;
                SODebugPacketBaseline packetBaseline = networkSendLayer.CaptureBaseline();
                yield return encodingLayer.RunLowFrequencyDebugImage(
                    this,
                    debugImageEncodingTimeoutSeconds,
                    pollIntervalSeconds,
                    (result, observation) =>
                    {
                        passed = result;
                        debugImage = observation;
                    });

                if (passed)
                {
                    yield return networkSendLayer.RunDebugImagePacket(
                        this,
                        debugImage,
                        packetBaseline,
                        debugImageNetworkTimeoutSeconds,
                        pollIntervalSeconds,
                        result => passed = result);
                }
            }

            yield return networkLayer.StopRecordingWindowIfNeeded(this, stopRecordingAtEnd, recordingDurationSeconds, recordingStopTimeoutSeconds, pollIntervalSeconds);

            if (passed)
            {
                SODebugLog.Pass(this, "Pipeline", SODebugLog.Fields(
                    "result=Data collection chain plus temporary Debug JPEG network route passed",
                    "encodingMode=DebugImageOnly",
                    "networkSendChecked=DebugImagePacketOnly"));
            }
            else
            {
                SODebugLog.Fail(
                    this,
                    "Pipeline",
                    "DebugImageTemporaryRoute",
                    "all checked layers pass",
                    "failed before completing temporary Debug JPEG route",
                    "A previous layer logged the exact SO fields that blocked the chain.",
                    0f);
            }

            activeRun = null;
        }

        private void ResetRuntimeStateBeforeRun()
        {
            if (runtimeStateResetter == null)
            {
                runtimeStateResetter = GetComponent<SORuntimeStateResetter>();
            }

            if (runtimeStateResetter == null)
            {
                Debug.Log(
                    "[SORuntimeStateReset] failed assetName=\"<none>\" assetPath=\"<none>\" method=\"ResetRuntimeState\" error=\"DataCaptureSODebugPipeline has no SORuntimeStateResetter assigned or attached.\"",
                    this);
                return;
            }

            runtimeStateResetter.ResetRuntimeState();
        }
    }
}
