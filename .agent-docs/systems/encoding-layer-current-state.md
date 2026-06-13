# Encoding Layer Current State

Last updated: 2026-06-13

## Purpose

This document points agents to the current runtime-video encoding workstream and records the high-level state after the 2026-06-10 passthrough-camera MP4 prototype validation.

Detailed handoff:

- `Assets/DataCapture/Runtime/40_SingleEncodeProduction/EncoderBackends/AndroidMediaCodec/current-findings-next-steps-2026-06-10.zh-CN.md`
- [Stage 04 Single Encode Production](stage-04-single-encode-production.md)

## Current Status

Validated:

- Quest Link / Play Mode can expose a real `PassthroughCameraAccess.GetTexture()` frame when the headset, Link, and PCA component are ready.
- Real PCA texture can be blitted into a staging `RenderTexture` and recorded to an MP4 through the InstantReplay prototype.
- `SampleScene` has `InstantReplayLocalMp4Recorder` mounted under `40_SingleEncodeProduction/30_Mp4MuxerOrVideoArtifactWriter`; it follows `RecordingSessionStateSO` and starts only when `EncodingPipelineConfigurationSO.outputMode == LocalMp4Save`.
- PC smoke tests show the data model can represent both stream `FramePacket` records and final `FileArtifact` records through `CaptureOutputQueueSO`.
- Stage 03's `MergedFrameSnapshotQueueSO` now exposes convenience reads for latest/sendable records and remains the single official input queue for stage 04.
- Stage 04 now has `SingleEncodeOutputQueueSO` plus `SingleEncodeStageBoundary` as its public exit contract.
- Stage 05 now has `SingleEncodeOutputProductBuilder` to consume stage 04's public output without reaching into stage 04 internals.
- `SampleScene` mounts `SingleEncodeStageBoundary` under `40_SingleEncodeProduction/60_StageBoundary` and `SingleEncodeOutputProductBuilder` under `50_ProductAssembly/30_SingleEncodeOutputProductBuilder`.
- `InstantReplayLocalMp4Recorder` now writes local MP4 lifecycle, final path, byte length, frame count, and failure/blocker text into `Mp4ArtifactWriterStateSO` while still publishing the legacy `EncodedOutputMetadataBinder -> CaptureOutputQueueSO` file artifact.

Not yet validated:

- `InstantReplayLocalMp4Recorder` currently has `androidPlayerOnly = true`; it is blocked in Unity Editor and non-Android players. Treat local MP4 as an Android Player / Quest-device capability, not an Editor capability.
- `20_TextureToAccessUnitEncoder` is still an empty scene node in `SampleScene`. `40_FrameIndexWriter` and `50_EncodingHealth` now have scene components, but real access-unit production/health remains unvalidated.
- Real PCA or composited `RenderTexture` into Android `MediaCodec` input `Surface`.
- One MediaCodec encode feeding both realtime network access units and `MediaMuxer` MP4 samples on Quest.
- Network transfer of finalized MP4 artifacts.
- Current `SampleScene` is sufficient to attempt the InstantReplay local MP4 bootstrap path on Quest Android Player, but not sufficient to prove the final access-unit bus or network video stream.

## Key Runtime Files

- `Assets/DataCapture/Runtime/40_SingleEncodeProduction/SynchronizedFrameReader/VideoFrameInputResolver.cs`
- `Assets/DataCapture/Runtime/40_SingleEncodeProduction/SynchronizedFrameReader/VideoEncodingParameterResolver.cs`
- `Assets/DataCapture/Runtime/40_SingleEncodeProduction/StageBoundary/SingleEncodeStageBoundary.cs`
- `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/40_SingleEncodeProduction/SingleEncodeOutputQueueSO.cs`
- `Assets/DataCapture/Runtime/50_ProductAssembly/SingleEncodeInput/SingleEncodeOutputProductBuilder.cs`
- `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/50_ProductAssembly/CaptureOutputQueueSO.cs`
- `Assets/DataCapture/Runtime/50_ProductAssembly/SessionArtifacts/CaptureOutputRecord.cs`
- `Assets/DataCapture/Runtime/40_SingleEncodeProduction/EncoderBackends/AndroidMediaCodec/SingleEncodeAndroidMuxerSmokeRunner.cs`
- `Assets/DataCapture/Runtime/40_SingleEncodeProduction/EncoderBackends/AndroidMediaCodec/Prototype/PassthroughCameraDirectMp4Prototype.cs`
- `Assets/Plugins/Android/com/q3datacapture/mediacodec/Q3SurfaceVideoEncoder.java`

## Next Work

Recommended order:

1. Re-test the new PCA texture readiness probe on a connected headset.
2. Re-test the InstantReplay orientation fix against real PCA frames.
3. Validate the Java `startWithMp4(...)` A0 path on Quest Android Player using a test pattern first.
4. Implement the minimum Unity `RenderTexture` to Android `MediaCodec` input `Surface` bridge.
5. Add output consumers for network frame packets, network file artifacts, and local artifact retention.

Implemented after the handoff note:

- `Assets/DataCapture/Runtime/40_SingleEncodeProduction/EncoderBackends/AndroidMediaCodec/Prototype/PassthroughCameraTextureReadinessProbe.cs`
- `PassthroughCameraDirectMp4Prototype` can optionally require the probe to be ready before starting.
- `Assets/Scenes/SingleEncodePcSmoke.unity` has the probe mounted on `PCA_DirectMp4Prototype_Runtime` with `requireReadyProbe = true`.

## Design Notes

The production center of gravity should be the encoded access-unit bus, not InstantReplay and not MP4 as the primary transport:

```text
MergedFrameSnapshotQueueSO
  -> stage 04 internals
       -> one MediaCodec encode
       -> H264/H265 access units
       -> MP4/video artifact
       -> timestamp/index sequence
  -> SingleEncodeOutputQueueSO
  -> stage 05 product assembly
```

InstantReplay remains useful for fast local MP4 validation, but it does not prove the single-encode stream-plus-MP4 target because it does not expose reusable access units for the existing network path.

Use [Stage 04 Single Encode Production](stage-04-single-encode-production.md) as the authoritative current-vs-target map for this layer.

Current `SampleScene` caveat:

```text
EncodingPipelineConfiguration.asset:
  outputMode = LocalMp4Save
  pipelineMode = VideoOnly
  videoEncoderBackend = AndroidMediaCodecH264

EncoderConfiguration.asset:
  codec = DEBUG_JPEG
  target = 320x320 @ 2fps
```

`VideoEncodingParameterResolver` resolves codec from `EncodingPipelineConfiguration.videoEncoderBackend`, so runtime codec resolves to H264 despite the stale `EncoderConfiguration.codec` label. Width, height, and frame rate still come from `EncoderConfiguration` unless valid camera stream state overrides them. This mismatch should be cleaned before judging final recording quality.
