using System;
using System.IO;
using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Networking
{
    public sealed class EncodedOutputMetadataBinder : MonoBehaviour
    {
        [Header("SO Inputs")]
        [SerializeField] private MergedFrameSnapshotQueueSO mergedSnapshotQueue;
        [SerializeField] private EncodedOutputBindingConfigurationSO bindingConfiguration;
        [SerializeField] private RecordingSessionStateSO recordingState;

        [Header("SO Outputs")]
        [SerializeField] private CurrentCaptureOutputSO currentOutput;
        [SerializeField] private CaptureOutputQueueSO outputQueue;

        [Header("Runtime Diagnostics")]
        [SerializeField] private long nextOutputId;
        [SerializeField] private int publishedCount;
        [SerializeField] private int failedBindingCount;
        [SerializeField] private string lastStatus;

        public bool PublishFramePacket(
            EncodedFrameRecord encodedFrame,
            byte[] payload,
            CapturePayloadKind payloadKind)
        {
            var config = EffectiveConfiguration;
            if (!ValidateMemoryPayload(config, payload, out string payloadError))
            {
                return PublishFailedFrame(encodedFrame, payloadKind, payloadError);
            }

            CaptureOutputRecord record = CreateBaseRecord(
                CaptureOutputKind.FramePacket,
                CaptureDeliveryKind.Stream,
                payloadKind,
                encodedFrame.timestampUnixMs);
            record.sourceCameraFrameId = encodedFrame.sourceCameraFrameId;
            record.sourceFrameStartId = encodedFrame.sourceCameraFrameId;
            record.sourceFrameEndId = encodedFrame.sourceCameraFrameId;
            record.timestampStartUnixMs = encodedFrame.timestampUnixMs;
            record.timestampEndUnixMs = encodedFrame.timestampUnixMs;
            record.codec = encodedFrame.codec;
            record.width = encodedFrame.width;
            record.height = encodedFrame.height;
            record.isKeyFrame = encodedFrame.isKeyFrame;
            record.byteLength = payload != null ? payload.Length : encodedFrame.byteLength;
            record.payloadRef = CapturePayloadRef.FromBytes(payload);
            record.metadataMode = CaptureMetadataMode.InlineSnapshot;

            if (!TryBindSnapshot(
                encodedFrame.sourceCameraFrameId,
                encodedFrame.timestampUnixMs,
                config,
                ref record))
            {
                return PublishFailedRecord(record, "Failed to bind synchronized metadata.");
            }

            record.status = CaptureOutputStatus.Ready;
            return PublishRecord(record);
        }

        public bool PublishFileArtifact(
            string filePath,
            string metadataSidecarPath,
            string manifestPath,
            long sourceFrameStartId,
            long sourceFrameEndId,
            long timestampStartUnixMs,
            long timestampEndUnixMs,
            string codec,
            int width,
            int height,
            int frameRate)
        {
            long timestamp = timestampEndUnixMs > 0 ? timestampEndUnixMs : timestampStartUnixMs;
            CaptureOutputRecord record = CreateBaseRecord(
                CaptureOutputKind.FileArtifact,
                CaptureDeliveryKind.OneShot,
                CapturePayloadKind.Mp4File,
                timestamp);
            record.sourceCameraFrameId = sourceFrameEndId;
            record.sourceFrameStartId = sourceFrameStartId;
            record.sourceFrameEndId = sourceFrameEndId;
            record.timestampStartUnixMs = timestampStartUnixMs;
            record.timestampEndUnixMs = timestampEndUnixMs;
            record.codec = codec ?? string.Empty;
            record.width = width;
            record.height = height;
            record.frameRate = frameRate;
            record.metadataMode = CaptureMetadataMode.SidecarFile;
            record.metadataSidecarPath = metadataSidecarPath ?? string.Empty;
            record.manifestPath = manifestPath ?? string.Empty;

            long byteLength = 0;
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                byteLength = new FileInfo(filePath).Length;
            }

            record.byteLength = byteLength > int.MaxValue ? int.MaxValue : (int)byteLength;
            record.payloadRef = CapturePayloadRef.FromLocalFile(filePath, byteLength);

            var config = EffectiveConfiguration;
            if (sourceFrameEndId >= 0 || timestamp > 0)
            {
                TryBindSnapshot(sourceFrameEndId, timestamp, config, ref record);
            }

            record.status = CaptureOutputStatus.Ready;
            return PublishRecord(record);
        }

        private EncodedOutputBindingConfigurationSO EffectiveConfiguration => bindingConfiguration;

        private CaptureOutputRecord CreateBaseRecord(
            CaptureOutputKind outputKind,
            CaptureDeliveryKind deliveryKind,
            CapturePayloadKind payloadKind,
            long timestampUnixMs)
        {
            return new CaptureOutputRecord
            {
                outputId = nextOutputId++,
                timestampUnixMs = timestampUnixMs,
                outputKind = outputKind,
                deliveryKind = deliveryKind,
                payloadKind = payloadKind,
                status = CaptureOutputStatus.Pending,
                sourceCameraFrameId = -1,
                sourceFrameStartId = -1,
                sourceFrameEndId = -1,
                codec = string.Empty,
                metadataSidecarPath = string.Empty,
                manifestPath = string.Empty,
                dropReason = string.Empty
            };
        }

        private bool TryBindSnapshot(
            long sourceFrameId,
            long timestampUnixMs,
            EncodedOutputBindingConfigurationSO config,
            ref CaptureOutputRecord record)
        {
            if (mergedSnapshotQueue == null)
            {
                record.metadataBindingStatus = CaptureMetadataBindingStatus.MissingSnapshot;
                record.dropReason = "MergedFrameSnapshotQueueSO is not assigned.";
                return !RequiresSendableSnapshot(config);
            }

            if (TryFindSnapshotByFrameId(sourceFrameId, out MergedFrameSnapshotRecord snapshot))
            {
                return ApplySnapshot(snapshot, CaptureMetadataBindingStatus.ExactFrameId, timestampUnixMs, config, ref record);
            }

            long tolerance = config != null ? config.maxTimestampDeltaMs : 10;
            snapshot = mergedSnapshotQueue.GetDataAt(timestampUnixMs, tolerance);
            if (snapshot.timestampUnixMs > 0)
            {
                return ApplySnapshot(snapshot, CaptureMetadataBindingStatus.TimestampFallback, timestampUnixMs, config, ref record);
            }

            record.metadataBindingStatus = CaptureMetadataBindingStatus.MissingSnapshot;
            record.dropReason = "No synchronized metadata snapshot for frame " + sourceFrameId + ".";
            return !RequiresSendableSnapshot(config);
        }

        private bool TryFindSnapshotByFrameId(long sourceFrameId, out MergedFrameSnapshotRecord snapshot)
        {
            snapshot = default;
            if (sourceFrameId < 0 || mergedSnapshotQueue == null)
            {
                return false;
            }

            MergedFrameSnapshotRecord[] records = mergedSnapshotQueue.ExportSnapshot();
            for (int i = records.Length - 1; i >= 0; i--)
            {
                if (records[i].frameId == sourceFrameId)
                {
                    snapshot = records[i];
                    return true;
                }
            }

            return false;
        }

        private bool ApplySnapshot(
            MergedFrameSnapshotRecord snapshot,
            CaptureMetadataBindingStatus bindingStatus,
            long timestampUnixMs,
            EncodedOutputBindingConfigurationSO config,
            ref CaptureOutputRecord record)
        {
            record.hasMergedSnapshot = true;
            record.mergedSnapshot = snapshot;
            record.metadataBindingStatus = bindingStatus;
            record.metadataTimestampDeltaMs = timestampUnixMs > 0
                ? snapshot.timestampUnixMs - timestampUnixMs
                : 0;

            if (RequiresSendableSnapshot(config) && !snapshot.isSendable)
            {
                record.metadataBindingStatus = CaptureMetadataBindingStatus.NotSendable;
                record.dropReason = string.IsNullOrWhiteSpace(snapshot.dropReason)
                    ? "Merged snapshot is not sendable."
                    : snapshot.dropReason;
                return config != null && config.allowPartialDebugSnapshot;
            }

            return true;
        }

        private bool RequiresSendableSnapshot(EncodedOutputBindingConfigurationSO config)
        {
            return config == null || config.requireSendableMergedSnapshot;
        }

        private bool ValidateMemoryPayload(
            EncodedOutputBindingConfigurationSO config,
            byte[] payload,
            out string error)
        {
            if (payload == null)
            {
                error = "Payload bytes are null.";
                return false;
            }

            if (config != null && !config.allowMemoryPayloads)
            {
                error = "Memory payloads are disabled by configuration.";
                return false;
            }

            int maxBytes = config != null ? config.maxMemoryPayloadBytes : 2 * 1024 * 1024;
            if (maxBytes > 0 && payload.Length > maxBytes)
            {
                error = "Payload bytes exceed maxMemoryPayloadBytes.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private bool PublishFailedFrame(
            EncodedFrameRecord encodedFrame,
            CapturePayloadKind payloadKind,
            string reason)
        {
            CaptureOutputRecord record = CreateBaseRecord(
                CaptureOutputKind.FramePacket,
                CaptureDeliveryKind.Stream,
                payloadKind,
                encodedFrame.timestampUnixMs);
            record.sourceCameraFrameId = encodedFrame.sourceCameraFrameId;
            record.codec = encodedFrame.codec;
            record.width = encodedFrame.width;
            record.height = encodedFrame.height;
            record.isKeyFrame = encodedFrame.isKeyFrame;
            return PublishFailedRecord(record, reason);
        }

        private bool PublishFailedRecord(CaptureOutputRecord record, string reason)
        {
            failedBindingCount++;
            record.status = CaptureOutputStatus.Failed;
            record.dropReason = reason ?? string.Empty;
            lastStatus = record.dropReason;

            if (bindingConfiguration != null && !bindingConfiguration.publishFailedRecordsForDiagnostics)
            {
                return false;
            }

            PublishRecord(record);
            return false;
        }

        private bool PublishRecord(CaptureOutputRecord record)
        {
            if (record.status == CaptureOutputStatus.Ready &&
                recordingState != null &&
                !recordingState.ShouldWriteQueues)
            {
                lastStatus = "Recording state does not allow writing capture output.";
                return false;
            }

            currentOutput?.SetRecord(record);
            outputQueue?.RecordData(record);
            publishedCount++;
            lastStatus = "Published capture output " + record.outputId + " (" + record.outputKind + ").";
            return record.status == CaptureOutputStatus.Ready;
        }
    }
}
