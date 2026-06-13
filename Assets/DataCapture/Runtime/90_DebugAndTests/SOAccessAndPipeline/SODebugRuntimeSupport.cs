using System.Collections;
using DataCapture.Networking;
using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Testing
{
    public readonly struct SODebugCurrentBaseline
    {
        public readonly ScriptableObject Asset;
        public readonly long TimestampUnixMs;
        public readonly long RecordSequence;

        public SODebugCurrentBaseline(ScriptableObject asset, long timestampUnixMs, long recordSequence)
        {
            Asset = asset;
            TimestampUnixMs = timestampUnixMs;
            RecordSequence = recordSequence;
        }
    }

    public readonly struct SODebugQueueBaseline
    {
        public readonly ScriptableObject Asset;
        public readonly int Count;
        public readonly long NewestTimestamp;
        public readonly long GenerationId;

        public SODebugQueueBaseline(ScriptableObject asset, int count, long newestTimestamp, long generationId)
        {
            Asset = asset;
            Count = count;
            NewestTimestamp = newestTimestamp;
            GenerationId = generationId;
        }
    }

    public readonly struct SODebugSyncBaseline
    {
        public readonly int MergedCount;
        public readonly long LatestCameraFrameId;
        public readonly int MergedQueueCount;
        public readonly long MergedQueueNewestTimestamp;

        public SODebugSyncBaseline(int mergedCount, long latestCameraFrameId, int mergedQueueCount, long mergedQueueNewestTimestamp)
        {
            MergedCount = mergedCount;
            LatestCameraFrameId = latestCameraFrameId;
            MergedQueueCount = mergedQueueCount;
            MergedQueueNewestTimestamp = mergedQueueNewestTimestamp;
        }
    }

    public readonly struct SODebugDebugImageBaseline
    {
        public readonly long SourceCameraFrameId;
        public readonly long EncodedFrameId;
        public readonly int EncodedQueueCount;
        public readonly long CaptureOutputId;
        public readonly int CaptureOutputQueueCount;

        public SODebugDebugImageBaseline(long sourceCameraFrameId, long encodedFrameId, int encodedQueueCount)
            : this(sourceCameraFrameId, encodedFrameId, encodedQueueCount, -1, 0)
        {
        }

        public SODebugDebugImageBaseline(
            long sourceCameraFrameId,
            long encodedFrameId,
            int encodedQueueCount,
            long captureOutputId,
            int captureOutputQueueCount)
        {
            SourceCameraFrameId = sourceCameraFrameId;
            EncodedFrameId = encodedFrameId;
            EncodedQueueCount = encodedQueueCount;
            CaptureOutputId = captureOutputId;
            CaptureOutputQueueCount = captureOutputQueueCount;
        }
    }

    public readonly struct SODebugDebugImageObservation
    {
        public readonly bool IsValid;
        public readonly long SourceCameraFrameId;
        public readonly long EncodedFrameId;
        public readonly long TimestampUnixMs;
        public readonly int Width;
        public readonly int Height;
        public readonly string Codec;
        public readonly int ByteLength;
        public readonly int EncodedQueueCount;
        public readonly string Source;

        public SODebugDebugImageObservation(
            bool isValid,
            long sourceCameraFrameId,
            long encodedFrameId,
            long timestampUnixMs,
            int width,
            int height,
            string codec,
            int byteLength,
            int encodedQueueCount,
            string source)
        {
            IsValid = isValid;
            SourceCameraFrameId = sourceCameraFrameId;
            EncodedFrameId = encodedFrameId;
            TimestampUnixMs = timestampUnixMs;
            Width = width;
            Height = height;
            Codec = codec;
            ByteLength = byteLength;
            EncodedQueueCount = encodedQueueCount;
            Source = source;
        }

        public static SODebugDebugImageObservation Empty =>
            new SODebugDebugImageObservation(false, -1, -1, 0, 0, 0, string.Empty, 0, 0, "<none>");
    }

    public readonly struct SODebugPacketBaseline
    {
        public readonly int PacketQueueCount;
        public readonly long CurrentSequenceId;
        public readonly long CurrentFrameId;

        public SODebugPacketBaseline(int packetQueueCount, long currentSequenceId, long currentFrameId)
        {
            PacketQueueCount = packetQueueCount;
            CurrentSequenceId = currentSequenceId;
            CurrentFrameId = currentFrameId;
        }
    }

    internal static class SODebugLog
    {
        public static void Action(MonoBehaviour owner, string layer, string action, string fields)
        {
            Debug.Log("[SO-Debug][ACTION][" + layer + "] action=" + action + " fields=" + fields, owner);
        }

        public static void Pass(MonoBehaviour owner, string layer, string fields)
        {
            Debug.Log("[SO-Debug][PASS][" + layer + "] fields=" + fields, owner);
        }

        public static void Fail(
            MonoBehaviour owner,
            string layer,
            string target,
            string condition,
            string actual,
            string blocker,
            float timeoutSeconds,
            string fields = "")
        {
            Debug.LogError(
                "[SO-Debug][FAIL][" + layer + "] target=" + target +
                " condition=" + condition +
                " actual=" + actual +
                " fields=" + fields +
                " blocker=" + blocker +
                " timeout=" + timeoutSeconds.ToString("0.0"),
                owner);
        }

        public static string Fields(params string[] parts)
        {
            return string.Join("; ", parts);
        }

        public static string FirstBlocker(string current, string next)
        {
            return string.IsNullOrWhiteSpace(current) ? next : current;
        }

        public static string NameOf(Object obj)
        {
            return obj != null ? obj.name : "<null>";
        }

        public static string Empty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "<empty>" : value;
        }

        public static string Bool(bool value)
        {
            return value ? "True" : "False";
        }
    }

    internal static class SODebugControllerButtons
    {
        public static IEnumerator Press(
            MonoBehaviour owner,
            CurrentControllerPoseSO pose,
            ControllerRecordingButtonBinding button,
            float holdSeconds,
            string layer,
            string reason,
            string extraFields)
        {
            if (pose == null)
            {
                SODebugLog.Fail(
                    owner,
                    layer,
                    "CurrentControllerPose",
                    "assigned",
                    "null",
                    "Cannot simulate controller button SO input.",
                    0f,
                    extraFields);
                yield break;
            }

            string fieldName = FieldName(button);
            Set(pose, button, true);
            SODebugLog.Action(owner, layer, "CurrentControllerPose." + fieldName, SODebugLog.Fields(
                "write=True",
                "button=" + button,
                "source=SODebugPipeline." + reason,
                extraFields));

            yield return new WaitForSecondsRealtime(Mathf.Max(0.02f, holdSeconds));

            Set(pose, button, false);
            SODebugLog.Action(owner, layer, "CurrentControllerPose." + fieldName, SODebugLog.Fields(
                "write=False",
                "button=" + button,
                "source=SODebugPipeline." + reason,
                extraFields));
        }

        private static void Set(CurrentControllerPoseSO pose, ControllerRecordingButtonBinding button, bool value)
        {
            switch (button)
            {
                case ControllerRecordingButtonBinding.LeftTrigger:
                    pose.leftTriggerPressed = value;
                    break;
                case ControllerRecordingButtonBinding.LeftGrip:
                    pose.leftGripPressed = value;
                    break;
                case ControllerRecordingButtonBinding.LeftPrimaryButton:
                    pose.leftPrimaryButtonPressed = value;
                    break;
                case ControllerRecordingButtonBinding.LeftSecondaryButton:
                    pose.leftSecondaryButtonPressed = value;
                    break;
                case ControllerRecordingButtonBinding.RightTrigger:
                    pose.rightTriggerPressed = value;
                    break;
                case ControllerRecordingButtonBinding.RightGrip:
                    pose.rightGripPressed = value;
                    break;
                case ControllerRecordingButtonBinding.RightPrimaryButton:
                    pose.rightPrimaryButtonPressed = value;
                    break;
                case ControllerRecordingButtonBinding.RightSecondaryButton:
                    pose.rightSecondaryButtonPressed = value;
                    break;
            }
        }

        private static string FieldName(ControllerRecordingButtonBinding button)
        {
            switch (button)
            {
                case ControllerRecordingButtonBinding.LeftTrigger:
                    return "leftTriggerPressed";
                case ControllerRecordingButtonBinding.LeftGrip:
                    return "leftGripPressed";
                case ControllerRecordingButtonBinding.LeftPrimaryButton:
                    return "leftPrimaryButtonPressed";
                case ControllerRecordingButtonBinding.LeftSecondaryButton:
                    return "leftSecondaryButtonPressed";
                case ControllerRecordingButtonBinding.RightTrigger:
                    return "rightTriggerPressed";
                case ControllerRecordingButtonBinding.RightGrip:
                    return "rightGripPressed";
                case ControllerRecordingButtonBinding.RightPrimaryButton:
                    return "rightPrimaryButtonPressed";
                case ControllerRecordingButtonBinding.RightSecondaryButton:
                    return "rightSecondaryButtonPressed";
                default:
                    return "unknownButton";
            }
        }
    }
}
