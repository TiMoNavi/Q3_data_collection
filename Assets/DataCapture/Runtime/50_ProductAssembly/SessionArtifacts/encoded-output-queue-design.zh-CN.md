# Encoded Output Queue Design

Last updated: 2026-06-12

## Role

`CaptureOutputQueueSO` is the public queue for encoded outputs. It is not the encoder itself and it is not a network sender.

Its job is to hold lightweight output records that downstream consumers can read with their own cursors.

## Record Types

```text
CaptureOutputRecord
  outputKind: FramePacket | FileArtifact
  deliveryKind: Stream | OneShot
  payloadKind: DebugJpeg | H264AccessUnit | H265AccessUnit | Mp4File | MetadataOnly
  payloadRef: MemoryBytes | LocalFile | PendingFile
  metadataMode: InlineSnapshot | SidecarFile
```

For the final video path:

```text
H264/H265 access unit
  -> CaptureOutputRecord(FramePacket, Stream, H264AccessUnit/H265AccessUnit)

final MP4
  -> CaptureOutputRecord(FileArtifact, OneShot, Mp4File)
```

## Relationship To The Final Architecture

The final architecture is the Single Encode Access Unit Bus:

```text
MediaCodec output access unit
  -> NetworkFramePacketSender
  -> MediaMuxer MP4 sample
  -> CaptureOutputQueueSO FramePacket

MediaMuxer finalized MP4
  -> CaptureOutputQueueSO FileArtifact
  -> LocalArtifactStore
  -> NetworkFileArtifactSender
```

`CaptureOutputQueueSO` should not force MP4 bytes or large files into memory. File artifacts should use local file references and sidecar metadata.

## Current Implemented Pieces

```text
EncodedOutput/CaptureOutputRecord.cs
EncodedOutput/CurrentCaptureOutputSO.cs
EncodedOutput/CaptureOutputQueueSO.cs
EncodedOutput/EncodedOutputBindingConfigurationSO.cs
EncodedOutput/EncodedOutputMetadataBinder.cs
OutputSink/CaptureOutputConsumerStateSO.cs
```

State assets:

```text
Assets/SOData/DataCapture/50_ProductAssembly/
Assets/SOData/DataCapture/60_Distribution/
```

## Not Yet Implemented

The consumer components are still future work:

```text
NetworkFramePacketSender
NetworkFileArtifactSender
LocalArtifactStore
```

Do not implement these before the Unity texture -> MediaCodec input Surface bridge produces real access units.
