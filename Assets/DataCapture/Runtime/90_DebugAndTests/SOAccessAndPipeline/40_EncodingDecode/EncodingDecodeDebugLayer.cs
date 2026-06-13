using System;
using System.Collections;
using System.Collections.Generic;
using DataCapture.Networking;
using DataCapture.Synchronization;
using SObasic;
using UnityEngine;

namespace DataCapture.Testing
{
    [Serializable]
    public sealed class EncodingDecodeDebugLayer
    {
        // Temporary route: low-frequency Debug JPEG only.
        //
        // This intentionally exposes no video/H264/H265 option while the real
        // video encoding layer is being rebuilt. The only allowed advancement is:
        // EncodingPipelineConfiguration.outputMode = DebugLowFpsImage
        // EncodingPipelineConfiguration.pipelineMode = DebugImageOnly
        // EncodingPipelineConfiguration.videoEncoderBackend = DebugJpeg

        private const string ExpectedCodec = "DEBUG_JPEG";

        [Header("Gate")]
        [SerializeField] private CaptureTransmissionGateSO transmissionGate;

        [Header("Encoding Config SOs")]
        [SerializeField] private EncodingPipelineConfigurationSO encodingConfiguration;
        [SerializeField] private DebugImageStreamSettingsSO debugImageSettings;
        [SerializeField] private float temporaryMaxFramesPerSecond = 2f;
        [SerializeField] private int temporaryMaxDimension = 320;
        [SerializeField, Range(1, 100)] private int temporaryJpegQuality = 70;
        [SerializeField] private bool requireSendableMergedSnapshot = true;

        [Header("Encoding Runtime Components")]
        [SerializeField] private AsyncDebugJpegNetworkStreamer debugImageStreamer;
        [SerializeField] private bool triggerStreamerFromDebugLayer = true;

        [Header("Encoding Input SOs")]
        [SerializeField] private CurrentVideoFrameInputSO currentVideoFrameInput;

        [Header("Encoding Result SOs")]
        [SerializeField] private CurrentEncodedFrameSO currentEncodedFrame;
        [SerializeField] private EncodedFrameQueueSO encodedFrameQueue;
        [SerializeField] private CurrentCaptureOutputSO currentCaptureOutput;
        [SerializeField] private CaptureOutputQueueSO captureOutputQueue;

        public SODebugDebugImageBaseline CaptureBaseline()
        {
            return new SODebugDebugImageBaseline(
                currentEncodedFrame != null ? currentEncodedFrame.sourceCameraFrameId : -1,
                currentEncodedFrame != null ? currentEncodedFrame.encodedFrameId : -1,
                encodedFrameQueue != null ? encodedFrameQueue.Count : 0,
                currentCaptureOutput != null && currentCaptureOutput.IsRecordValid ? currentCaptureOutput.Current.outputId : -1,
                captureOutputQueue != null ? captureOutputQueue.Count : 0);
        }

        public IEnumerator RunLowFrequencyDebugImage(
            MonoBehaviour owner,
            float timeoutSeconds,
            float pollIntervalSeconds,
            Action<bool, SODebugDebugImageObservation> complete)
        {
            SODebugDebugImageBaseline baseline = CaptureBaseline();
            if (!TryApplyTemporaryMode(owner, out string applyError))
            {
                SODebugLog.Fail(
                    owner,
                    "Encoding.DebugImage",
                    "EncodingPipelineConfiguration",
                    "DebugImageOnly + DebugJpeg writable",
                    "write failed",
                    applyError,
                    0f,
                    BuildFields(baseline, SODebugDebugImageObservation.Empty));
                complete(false, SODebugDebugImageObservation.Empty);
                yield break;
            }

            float deadline = Time.unscaledTime + Mathf.Max(0.1f, timeoutSeconds);
            SODebugDebugImageObservation latest = SODebugDebugImageObservation.Empty;
            SODebugCaptureOutputObservation latestOutput = SODebugCaptureOutputObservation.Empty;
            string latestBlocker = string.Empty;

            while (Time.unscaledTime < deadline)
            {
                TryTriggerDebugImage(owner);

                latest = ObserveLatestDebugImage();
                latestOutput = ObserveLatestCaptureOutput();
                if (!IsGateOpen(out string gateBlocker))
                {
                    latestBlocker = gateBlocker;
                }
                else if (!IsVideoInputReady(out string inputBlocker))
                {
                    latestBlocker = inputBlocker;
                }
                else
                {
                    bool frameOk = IsExpectedDebugImage(latest, baseline, out string loopFrameBlocker);
                    bool outputOk = IsExpectedCaptureOutput(latestOutput, latest, baseline, out string loopOutputBlocker);
                    if (frameOk && outputOk)
                    {
                        SODebugLog.Pass(owner, "Encoding.DebugImage", BuildFields(baseline, latest, latestOutput));
                        complete(true, latest);
                        yield break;
                    }

                    latestBlocker = SODebugLog.FirstBlocker(loopFrameBlocker, loopOutputBlocker);
                }

                yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, pollIntervalSeconds));
            }

            latest = ObserveLatestDebugImage();
            latestOutput = ObserveLatestCaptureOutput();
            IsExpectedDebugImage(latest, baseline, out string frameBlocker);
            IsExpectedCaptureOutput(latestOutput, latest, baseline, out string outputFinalBlocker);
            string blocker = !IsGateOpen(out string gateFinalBlocker)
                ? gateFinalBlocker
                : !IsVideoInputReady(out string inputFinalBlocker)
                    ? inputFinalBlocker
                    : SODebugLog.FirstBlocker(frameBlocker, outputFinalBlocker);

            SODebugLog.Fail(
                owner,
                "Encoding.DebugImage",
                "CurrentEncodedFrame + CurrentCaptureOutput",
                "codec==DEBUG_JPEG && byteLength>0 && frame advances && capture output Ready DebugJpeg",
                BuildActual(latest, latestOutput),
                SODebugLog.FirstBlocker(latestBlocker, blocker),
                timeoutSeconds,
                BuildFields(baseline, latest, latestOutput));
            complete(false, latest);
        }

        private bool TryApplyTemporaryMode(MonoBehaviour owner, out string error)
        {
            error = string.Empty;
            if (encodingConfiguration == null)
            {
                error = "EncodingPipelineConfigurationSO is not assigned.";
                return false;
            }

            if (!SOValueAccessUtility.TryWrite(encodingConfiguration, "outputMode", CaptureVideoOutputMode.DebugLowFpsImage, out error))
            {
                return false;
            }

            if (!SOValueAccessUtility.TryWrite(encodingConfiguration, "pipelineMode", EncodingPipelineMode.DebugImageOnly, out error))
            {
                return false;
            }

            if (!SOValueAccessUtility.TryWrite(encodingConfiguration, "videoEncoderBackend", VideoEncoderBackend.DebugJpeg, out error))
            {
                return false;
            }

            if (!SOValueAccessUtility.TryWrite(encodingConfiguration, "allowDebugImageDuringVideo", false, out error))
            {
                return false;
            }

            if (debugImageSettings != null)
            {
                if (!SOValueAccessUtility.TryWrite(debugImageSettings, "maxFramesPerSecond", temporaryMaxFramesPerSecond, out error))
                {
                    return false;
                }

                if (!SOValueAccessUtility.TryWrite(debugImageSettings, "maxDimension", temporaryMaxDimension, out error))
                {
                    return false;
                }

                if (!SOValueAccessUtility.TryWrite(debugImageSettings, "jpegQuality", temporaryJpegQuality, out error))
                {
                    return false;
                }

                if (!SOValueAccessUtility.TryWrite(debugImageSettings, "requireSendableMergedSnapshot", requireSendableMergedSnapshot, out error))
                {
                    return false;
                }
            }

            SODebugLog.Action(owner, "Encoding.DebugImage", "ApplyTemporaryDebugImageMode", SODebugLog.Fields(
                "outputMode=DebugLowFpsImage",
                "pipelineMode=DebugImageOnly",
                "videoEncoderBackend=DebugJpeg",
                "allowDebugImageDuringVideo=False",
                "maxFramesPerSecond=" + temporaryMaxFramesPerSecond.ToString("0.0"),
                "maxDimension=" + temporaryMaxDimension,
                "jpegQuality=" + temporaryJpegQuality,
                "requireSendableMergedSnapshot=" + SODebugLog.Bool(requireSendableMergedSnapshot),
                "writeMethod=SOValueAccessUtility"));
            return true;
        }

        private bool IsGateOpen(out string blocker)
        {
            if (transmissionGate == null)
            {
                blocker = "CaptureTransmissionGateSO is not assigned.";
                return false;
            }

            if (transmissionGate.canEncodeAndSend)
            {
                blocker = string.Empty;
                return true;
            }

            blocker = "CaptureTransmissionGateSO.canEncodeAndSend is false: " + SODebugLog.Empty(transmissionGate.activeBlocker);
            return false;
        }

        private bool TryTriggerDebugImage(MonoBehaviour owner)
        {
            if (!triggerStreamerFromDebugLayer || debugImageStreamer == null)
            {
                return false;
            }

            bool queued = debugImageStreamer.TryQueueFrame();
            if (queued)
            {
                SODebugLog.Action(owner, "Encoding.DebugImage", "AsyncDebugJpegNetworkStreamer.TryQueueFrame", SODebugLog.Fields(
                    "queued=True",
                    "input.sourceCameraFrameId=" + (currentVideoFrameInput != null ? currentVideoFrameInput.sourceCameraFrameId.ToString() : "null"),
                    "input.timestampUnixMs=" + (currentVideoFrameInput != null ? currentVideoFrameInput.timestampUnixMs.ToString() : "null")));
            }

            return queued;
        }

        private bool IsVideoInputReady(out string blocker)
        {
            if (currentVideoFrameInput == null)
            {
                blocker = "CurrentVideoFrameInputSO is not assigned.";
                return false;
            }

            if (!currentVideoFrameInput.isValid)
            {
                blocker = "CurrentVideoFrameInputSO.isValid is false. VideoFrameInputResolver may not have resolved a camera/composite frame.";
                return false;
            }

            if (currentVideoFrameInput.inputTexture == null)
            {
                blocker = "CurrentVideoFrameInputSO.inputTexture is null.";
                return false;
            }

            if (currentVideoFrameInput.sourceCameraFrameId < 0 || currentVideoFrameInput.timestampUnixMs <= 0)
            {
                blocker = "CurrentVideoFrameInputSO source frame id/timestamp is invalid.";
                return false;
            }

            blocker = string.Empty;
            return true;
        }

        private SODebugDebugImageObservation ObserveLatestDebugImage()
        {
            SODebugDebugImageObservation current = ObserveCurrentEncodedFrame();
            if (current.IsValid && current.Codec == ExpectedCodec)
            {
                return current;
            }

            if (TryFindQueuedDebugImage(out SODebugDebugImageObservation queued))
            {
                return queued;
            }

            return current;
        }

        private SODebugDebugImageObservation ObserveCurrentEncodedFrame()
        {
            if (currentEncodedFrame == null)
            {
                return SODebugDebugImageObservation.Empty;
            }

            return new SODebugDebugImageObservation(
                currentEncodedFrame.isValid,
                currentEncodedFrame.sourceCameraFrameId,
                currentEncodedFrame.encodedFrameId,
                currentEncodedFrame.timestampUnixMs,
                currentEncodedFrame.width,
                currentEncodedFrame.height,
                currentEncodedFrame.codec,
                currentEncodedFrame.byteLength,
                encodedFrameQueue != null ? encodedFrameQueue.Count : 0,
                "CurrentEncodedFrameSO");
        }

        private bool TryFindQueuedDebugImage(out SODebugDebugImageObservation observation)
        {
            observation = SODebugDebugImageObservation.Empty;
            if (encodedFrameQueue == null)
            {
                return false;
            }

            EncodedFrameRecord[] records = encodedFrameQueue.DebugSnapshot;
            if (records == null || records.Length == 0)
            {
                return false;
            }

            for (int i = records.Length - 1; i >= 0; i--)
            {
                EncodedFrameRecord record = records[i];
                if (record.codec != ExpectedCodec)
                {
                    continue;
                }

                observation = new SODebugDebugImageObservation(
                    true,
                    record.sourceCameraFrameId,
                    record.encodedFrameId,
                    record.timestampUnixMs,
                    record.width,
                    record.height,
                    record.codec,
                    record.byteLength,
                    encodedFrameQueue.Count,
                    "EncodedFrameQueueSO.DebugSnapshot");
                return true;
            }

            return false;
        }

        private SODebugCaptureOutputObservation ObserveLatestCaptureOutput()
        {
            SODebugCaptureOutputObservation current = ObserveCurrentCaptureOutput();
            if (current.IsReadyDebugJpeg)
            {
                return current;
            }

            if (TryFindQueuedCaptureOutput(out SODebugCaptureOutputObservation queued))
            {
                return queued;
            }

            return current;
        }

        private SODebugCaptureOutputObservation ObserveCurrentCaptureOutput()
        {
            if (currentCaptureOutput == null)
            {
                return SODebugCaptureOutputObservation.Empty;
            }

            return SODebugCaptureOutputObservation.FromRecord(
                currentCaptureOutput.IsRecordValid,
                currentCaptureOutput.Current,
                captureOutputQueue != null ? captureOutputQueue.Count : 0,
                "CurrentCaptureOutputSO");
        }

        private bool TryFindQueuedCaptureOutput(out SODebugCaptureOutputObservation observation)
        {
            observation = SODebugCaptureOutputObservation.Empty;
            if (captureOutputQueue == null)
            {
                return false;
            }

            CaptureOutputRecord[] records = captureOutputQueue.DebugSnapshot;
            if (records == null || records.Length == 0)
            {
                return false;
            }

            for (int i = records.Length - 1; i >= 0; i--)
            {
                CaptureOutputRecord record = records[i];
                if (record.payloadKind != CapturePayloadKind.DebugJpeg)
                {
                    continue;
                }

                observation = SODebugCaptureOutputObservation.FromRecord(
                    record.status == CaptureOutputStatus.Ready,
                    record,
                    captureOutputQueue.Count,
                    "CaptureOutputQueueSO.DebugSnapshot");
                return true;
            }

            return false;
        }

        private static bool IsExpectedDebugImage(
            SODebugDebugImageObservation observation,
            SODebugDebugImageBaseline baseline,
            out string blocker)
        {
            if (!observation.IsValid)
            {
                blocker = "No valid encoded Debug JPEG frame has been observed.";
                return false;
            }

            if (observation.Codec != ExpectedCodec)
            {
                blocker = "Encoded frame codec is " + SODebugLog.Empty(observation.Codec) + ", not DEBUG_JPEG.";
                return false;
            }

            if (observation.ByteLength <= 0)
            {
                blocker = "Debug JPEG byteLength is not positive.";
                return false;
            }

            bool advanced = observation.SourceCameraFrameId > baseline.SourceCameraFrameId ||
                observation.EncodedFrameId > baseline.EncodedFrameId ||
                observation.EncodedQueueCount > baseline.EncodedQueueCount;
            if (!advanced)
            {
                blocker = "Debug JPEG output did not advance beyond baseline.";
                return false;
            }

            blocker = string.Empty;
            return true;
        }

        private static bool IsExpectedCaptureOutput(
            SODebugCaptureOutputObservation output,
            SODebugDebugImageObservation debugImage,
            SODebugDebugImageBaseline baseline,
            out string blocker)
        {
            if (!output.IsValid)
            {
                blocker = "No valid CurrentCaptureOutput/CaptureOutputQueue DebugJpeg record has been observed.";
                return false;
            }

            if (output.PayloadKind != CapturePayloadKind.DebugJpeg)
            {
                blocker = "Capture output payloadKind is " + output.PayloadKind + ", not DebugJpeg.";
                return false;
            }

            if (output.Status != CaptureOutputStatus.Ready)
            {
                blocker = "Capture output status is " + output.Status + ": " + SODebugLog.Empty(output.DropReason);
                return false;
            }

            if (output.ByteLength <= 0)
            {
                blocker = "Capture output byteLength is not positive.";
                return false;
            }

            if (debugImage.IsValid && output.SourceCameraFrameId != debugImage.SourceCameraFrameId)
            {
                blocker = "Capture output sourceCameraFrameId does not match DEBUG_JPEG encoded frame.";
                return false;
            }

            bool advanced = output.OutputId > baseline.CaptureOutputId ||
                output.QueueCount > baseline.CaptureOutputQueueCount;
            if (!advanced)
            {
                blocker = "Capture output did not advance beyond baseline.";
                return false;
            }

            blocker = string.Empty;
            return true;
        }

        private static string BuildActual(SODebugDebugImageObservation observation, SODebugCaptureOutputObservation output)
        {
            return "isValid=" + SODebugLog.Bool(observation.IsValid) +
                ", codec=" + SODebugLog.Empty(observation.Codec) +
                ", sourceCameraFrameId=" + observation.SourceCameraFrameId +
                ", byteLength=" + observation.ByteLength +
                ", captureOutput.isValid=" + SODebugLog.Bool(output.IsValid) +
                ", captureOutput.payloadKind=" + output.PayloadKind +
                ", captureOutput.status=" + output.Status +
                ", captureOutput.byteLength=" + output.ByteLength;
        }

        private string BuildFields(SODebugDebugImageBaseline baseline, SODebugDebugImageObservation observation)
        {
            return BuildFields(baseline, observation, ObserveLatestCaptureOutput());
        }

        private string BuildFields(
            SODebugDebugImageBaseline baseline,
            SODebugDebugImageObservation observation,
            SODebugCaptureOutputObservation output)
        {
            var parts = new List<string>
            {
                "outputMode=" + (encodingConfiguration != null ? encodingConfiguration.outputMode.ToString() : "null"),
                "pipelineMode=" + (encodingConfiguration != null ? encodingConfiguration.pipelineMode.ToString() : "null"),
                "videoEncoderBackend=" + (encodingConfiguration != null ? encodingConfiguration.videoEncoderBackend.ToString() : "null"),
                "allowDebugImageDuringVideo=" + (encodingConfiguration != null ? SODebugLog.Bool(encodingConfiguration.allowDebugImageDuringVideo) : "null"),
                "allowsDebugImage=" + (encodingConfiguration != null ? SODebugLog.Bool(encodingConfiguration.AllowsDebugImage) : "null"),
                "debugImage.maxFramesPerSecond=" + (debugImageSettings != null ? debugImageSettings.maxFramesPerSecond.ToString("0.0") : "null"),
                "debugImage.maxDimension=" + (debugImageSettings != null ? debugImageSettings.maxDimension.ToString() : "null"),
                "debugImage.jpegQuality=" + (debugImageSettings != null ? debugImageSettings.jpegQuality.ToString() : "null"),
                "debugImage.requireSendableMergedSnapshot=" + (debugImageSettings != null ? SODebugLog.Bool(debugImageSettings.requireSendableMergedSnapshot) : "null"),
                "streamer.assigned=" + SODebugLog.Bool(debugImageStreamer != null),
                "streamer.triggerFromDebugLayer=" + SODebugLog.Bool(triggerStreamerFromDebugLayer),
                "input.assigned=" + SODebugLog.Bool(currentVideoFrameInput != null),
                "input.isValid=" + (currentVideoFrameInput != null ? SODebugLog.Bool(currentVideoFrameInput.isValid) : "null"),
                "input.texture=" + SODebugLog.Empty(currentVideoFrameInput != null && currentVideoFrameInput.inputTexture != null ? currentVideoFrameInput.inputTexture.name : null),
                "input.sourceKind=" + (currentVideoFrameInput != null ? currentVideoFrameInput.sourceKind.ToString() : "null"),
                "input.sourceCameraFrameId=" + (currentVideoFrameInput != null ? currentVideoFrameInput.sourceCameraFrameId.ToString() : "null"),
                "input.timestampUnixMs=" + (currentVideoFrameInput != null ? currentVideoFrameInput.timestampUnixMs.ToString() : "null"),
                "input.sourceResolution=" + (currentVideoFrameInput != null ? currentVideoFrameInput.sourceResolution.x + "x" + currentVideoFrameInput.sourceResolution.y : "null"),
                "input.outputResolution=" + (currentVideoFrameInput != null ? currentVideoFrameInput.outputResolution.x + "x" + currentVideoFrameInput.outputResolution.y : "null"),
                "input.codec=" + SODebugLog.Empty(currentVideoFrameInput != null ? currentVideoFrameInput.codec : null),
                "gate.outputRouteReady=" + (transmissionGate != null ? SODebugLog.Bool(transmissionGate.outputRouteReady) : "null"),
                "gate.recordingActive=" + (transmissionGate != null ? SODebugLog.Bool(transmissionGate.recordingActive) : "null"),
                "gate.synthesisHealthy=" + (transmissionGate != null ? SODebugLog.Bool(transmissionGate.synthesisHealthy) : "null"),
                "gate.canEncodeAndSend=" + (transmissionGate != null ? SODebugLog.Bool(transmissionGate.canEncodeAndSend) : "null"),
                "gate.activeBlocker=" + SODebugLog.Empty(transmissionGate != null ? transmissionGate.activeBlocker : null),
                "baseline.sourceCameraFrameId=" + baseline.SourceCameraFrameId,
                "baseline.encodedFrameId=" + baseline.EncodedFrameId,
                "baseline.encodedQueueCount=" + baseline.EncodedQueueCount,
                "baseline.captureOutputId=" + baseline.CaptureOutputId,
                "baseline.captureOutputQueueCount=" + baseline.CaptureOutputQueueCount,
                "encoded.source=" + observation.Source,
                "encoded.isValid=" + SODebugLog.Bool(observation.IsValid),
                "encoded.sourceCameraFrameId=" + observation.SourceCameraFrameId,
                "encoded.encodedFrameId=" + observation.EncodedFrameId,
                "encoded.timestampUnixMs=" + observation.TimestampUnixMs,
                "encoded.codec=" + SODebugLog.Empty(observation.Codec),
                "encoded.byteLength=" + observation.ByteLength,
                "encoded.size=" + observation.Width + "x" + observation.Height,
                "encodedQueue.count=" + (encodedFrameQueue != null ? encodedFrameQueue.Count.ToString() : "null"),
                "captureOutput.source=" + output.Source,
                "captureOutput.isValid=" + SODebugLog.Bool(output.IsValid),
                "captureOutput.outputId=" + output.OutputId,
                "captureOutput.sourceCameraFrameId=" + output.SourceCameraFrameId,
                "captureOutput.timestampUnixMs=" + output.TimestampUnixMs,
                "captureOutput.payloadKind=" + output.PayloadKind,
                "captureOutput.status=" + output.Status,
                "captureOutput.byteLength=" + output.ByteLength,
                "captureOutput.hasMergedSnapshot=" + SODebugLog.Bool(output.HasMergedSnapshot),
                "captureOutput.metadataBindingStatus=" + output.MetadataBindingStatus,
                "captureOutput.dropReason=" + SODebugLog.Empty(output.DropReason),
                "captureOutputQueue.count=" + (captureOutputQueue != null ? captureOutputQueue.Count.ToString() : "null")
            };

            return string.Join("; ", parts);
        }

        private readonly struct SODebugCaptureOutputObservation
        {
            public readonly bool IsValid;
            public readonly long OutputId;
            public readonly long SourceCameraFrameId;
            public readonly long TimestampUnixMs;
            public readonly CapturePayloadKind PayloadKind;
            public readonly CaptureOutputStatus Status;
            public readonly int ByteLength;
            public readonly bool HasMergedSnapshot;
            public readonly CaptureMetadataBindingStatus MetadataBindingStatus;
            public readonly string DropReason;
            public readonly int QueueCount;
            public readonly string Source;

            public bool IsReadyDebugJpeg =>
                IsValid &&
                PayloadKind == CapturePayloadKind.DebugJpeg &&
                Status == CaptureOutputStatus.Ready &&
                ByteLength > 0;

            private SODebugCaptureOutputObservation(
                bool isValid,
                long outputId,
                long sourceCameraFrameId,
                long timestampUnixMs,
                CapturePayloadKind payloadKind,
                CaptureOutputStatus status,
                int byteLength,
                bool hasMergedSnapshot,
                CaptureMetadataBindingStatus metadataBindingStatus,
                string dropReason,
                int queueCount,
                string source)
            {
                IsValid = isValid;
                OutputId = outputId;
                SourceCameraFrameId = sourceCameraFrameId;
                TimestampUnixMs = timestampUnixMs;
                PayloadKind = payloadKind;
                Status = status;
                ByteLength = byteLength;
                HasMergedSnapshot = hasMergedSnapshot;
                MetadataBindingStatus = metadataBindingStatus;
                DropReason = dropReason ?? string.Empty;
                QueueCount = queueCount;
                Source = source;
            }

            public static SODebugCaptureOutputObservation FromRecord(
                bool isValid,
                CaptureOutputRecord record,
                int queueCount,
                string source)
            {
                return new SODebugCaptureOutputObservation(
                    isValid,
                    record.outputId,
                    record.sourceCameraFrameId,
                    record.timestampUnixMs,
                    record.payloadKind,
                    record.status,
                    record.byteLength,
                    record.hasMergedSnapshot,
                    record.metadataBindingStatus,
                    record.dropReason,
                    queueCount,
                    source);
            }

            public static SODebugCaptureOutputObservation Empty =>
                new SODebugCaptureOutputObservation(
                    false,
                    -1,
                    -1,
                    0,
                    CapturePayloadKind.None,
                    CaptureOutputStatus.Pending,
                    0,
                    false,
                    CaptureMetadataBindingStatus.None,
                    string.Empty,
                    0,
                    "<none>");
        }
    }
}
