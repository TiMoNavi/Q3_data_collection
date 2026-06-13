# Stage 04 Single Encode Production

Last updated: 2026-06-13

## Purpose

Stage 04 turns the synchronized video input chosen by stage 03 into encoded video products. It must eventually produce one authoritative encode stream that can feed both realtime network delivery and local MP4/session artifacts.

Meta Quest context verified via Meta docs: the Passthrough Camera API gives Unity access to a live camera texture and related camera data. It does not provide a ready-made H264/H265 stream, so this project must still own the encoding, muxing, indexing, and product validation path.

## Current Main Scene State

`Assets/Scenes/SampleScene.unity` currently has this stage-04 shape:

```text
40_SingleEncodeProduction
  00_SynchronizedFrameReader          -> VideoFrameInputResolver
  10_SingleRenderTextureBuilder       -> PassthroughCameraLayerCompositor
  20_TextureToAccessUnitEncoder       -> empty node
  30_Mp4MuxerOrVideoArtifactWriter    -> InstantReplayLocalMp4Recorder
  40_FrameIndexWriter                 -> FrameIndexWriter
  50_EncodingHealth                   -> SingleEncodeHealthReporter
  60_StageBoundary                    -> SingleEncodeStageBoundary
```

Stage 04 is not the producer of the virtual layer. The active scene now keeps the formal frame ownership chain as:

```text
01 Current SO Inputs
  -> CurrentCameraImageSO / CurrentVirtualLayerFrameSO
  -> 02 Queue Buffers
  -> CameraImageQueueSO / VirtualLayerQueueSO
  -> 03 TimestampMerger
  -> MergedFrameSnapshotQueueSO
       includes the matched CameraImageFrameRecord and VirtualLayerFrameRecord
  -> 04 VideoFrameInputResolver
       consumes the latest sendable MergedFrameSnapshotRecord
       blocks when the synchronized camera/virtual-layer frame texture is missing
  -> CurrentVideoFrameInputSO
  -> InstantReplayLocalMp4Recorder
  -> local .mp4 + .metadata.jsonl + .manifest.json
  -> Mp4ArtifactWriterStateSO
  -> SessionArtifactManifestBuilder / SessionFinalizeController
```

This is a useful first-output path, but it is still a bootstrap path. The important boundary is that the stage-04 resolver no longer treats a live compositor/current SO as the formal input; its formal input is the stage-03 synchronized queue.

## Can The Scene Produce An MP4 Now?

Conditionally yes, on Quest Android Player.

Required conditions:

- Build and run on Android Player / Quest. `InstantReplayLocalMp4Recorder.androidPlayerOnly = true`, so the Unity Editor and non-Android players are explicitly blocked.
- `EncodingPipelineConfiguration.asset.outputMode = LocalMp4Save`. The current asset is already set this way.
- `RecordingSessionStateSO` must enter `Recording`. The recorder follows that state and starts automatically only while recording is active.
- `VideoFrameInputResolver` must publish a valid `CurrentVideoFrameInputSO`: valid synchronized source texture, even encoder dimensions, valid frame rate, and bitrate.
- Stage 01/02/03 must already be producing sendable `MergedFrameSnapshotRecord` entries. Composite mode requires each sendable snapshot to include both `cameraImage.texture` and `virtualLayer.texture`.

Current caveats:

- `EncoderConfiguration.asset` still carries `codec = DEBUG_JPEG`, `target = 320x320 @ 2fps`. Runtime codec is resolved from `EncodingPipelineConfiguration.videoEncoderBackend = AndroidMediaCodecH264`, but width/height/fps may still come from the stale encoder config unless camera stream state overrides them.
- `InstantReplayLocalMp4Recorder` writes `Mp4ArtifactWriterStateSO` on start/finalize/failure, so stage 05 can distinguish complete MP4 from blocked/missing output.
- The path does not prove realtime H264/H265 streaming and does not populate `EncodedAccessUnitQueueSO`.
- `10_SingleRenderTextureBuilder/PassthroughCameraLayerCompositor` is no longer allowed to publish `CurrentVirtualLayerFrameSO`. Virtual-layer production belongs to `10_CurrentSOInputs/10_VirtualLayer_CurrentWriter`; 04 may only consume the virtual-layer frame selected by stage 03.
- `FrameIndexWriter` is mounted and writes `FrameIndexSO`. It prefers `EncodedAccessUnitQueueSO` for strict access-unit-to-metadata mapping. For the current InstantReplay local MP4 bootstrap path, it rebuilds MP4 sample order from the recorder's `.metadata.jsonl` sidecar after finalization.
- `SingleEncodeStageBoundary` preserves the stage-03 `MetadataTimelineJournalSO` and the resolved `FrameIndexSO` in the stage-04 public output. If the writer has not produced entries yet, it can still derive a fallback stage-05 frame-index sequence from access units, metadata timeline entries, or synchronized snapshots.

## Automated Local MP4 Diagnostics

There are now two separate debug runners, and they should not be confused:

- `DataCaptureSODebugPipeline` on `SO_Debug_Probe` still verifies the older SO debug route: 00 recording control, 10 current inputs, 20 queues, 30 synchronization, then temporary Debug JPEG encoding/network packet checks. It does not validate local MP4 or stage 05.
- `LocalMp4EndToEndDebugRunner` is mounted at `DataCapture_Runtime/90_DebugAndTests/20_SmokeTests/Local_MP4_New01_to_05_Debug_Run` with `runOnStart = false`. This is the new local MP4 diagnostic path.

`LocalMp4EndToEndDebugRunner` uses `RecordingToggleRequestSO` only as a harness to open/close the recording window. The actual pass/fail chain starts at the rebuilt `10_CurrentSOInputs` layer:

```text
10 Current SO Inputs
  -> 20 Queue Buffers
  -> 30 Synchronization
  -> 40 Local MP4 bootstrap writer
  -> 50 Session artifact manifest / finalize gate
```

The runner forces local mode before a run:

- `SessionModeSO = LocalOnly`
- `NetworkSenderConfigurationSO.outputTarget = LocalFile`
- `EncodingPipelineConfigurationSO.outputMode = LocalMp4Save`
- `EncodingPipelineConfigurationSO.pipelineMode = VideoOnly`
- `EncodingPipelineConfigurationSO.videoEncoderBackend = AndroidMediaCodecH264`

Expected device behavior:

- In Unity Editor it should fail at the MP4 writer gate with the explicit Android Player blocker from `InstantReplayLocalMp4Recorder`.
- On Quest Android Player it can drive the recording window, wait for valid stage-01/02/03 data, wait for `Mp4ArtifactWriterStateSO`, stop recording, wait for finalize, and then ask stage 05 to evaluate.
- It does not relax stage 05. If the MP4 file is produced but stage 04 cannot publish metadata timeline entries or a resolved frame-index sequence, the runner should fail at `LocalMp4E2E.05` and print the `SessionFinalizeStateSO` / `SessionArtifactManifestSO` blocker fields.

## Current Android / Vulkan Evidence

The repository contains Android MediaCodec and Vulkan bridge work:

- `Q3SurfaceVideoEncoder.java` can create a `MediaCodec` encoder with `COLOR_FormatSurface`, attach an input `Surface`, drain encoded access-unit bytes, and optionally mux samples to MP4.
- Java synthetic-pattern encoding through EGL/GLES into the `MediaCodec` input surface has been validated in smoke paths.
- `libq3dc_vulkan_bridge.so` exists, and smoke code probes Unity Vulkan texture access.
- `UnityDynamicTextureEncoderSmokeRunner` exists in `SingleEncodePcSmoke.unity`, not in `SampleScene`.

Important boundary:

- `Q3SurfaceVideoEncoder.encodeUnityTextureFrame(...)` currently returns that the Unity texture bridge is not implemented yet.
- The missing production step is the actual Unity/PCA/composited `RenderTexture` write into the Android encoder input `Surface` through Vulkan copy/blit/render-pass logic.

## Required Real Production Chain

The target chain should be:

```text
MergedFrameSnapshotQueueSO
  -> SynchronizedFrameReader
       resolves a stable frameId/sourceTimestamp/video input reference from stage 03 only
  -> SingleRenderTextureBuilder
       builds raw / composite RenderTexture for that exact synchronized frame
  -> TextureToAccessUnitEncoder
       writes the texture to Android MediaCodec input Surface
       emits EncodedAccessUnitRecord records
  -> EncodedAccessUnitQueueSO
       one authoritative H264/H265 access-unit stream
  -> Mp4MuxerOrVideoArtifactWriter
       consumes the same access units and writes MP4
  -> FrameIndexWriter
       maps frameId/sourceTimestamp/accessUnitId/encodedPts/mp4SampleIndex/metadataTimelineEntryId
  -> EncodingHealthStateSO
       reports encoder/muxer/drop/PTS failures
  -> SingleEncodeStageBoundary
       publishes SingleEncodeOutputQueueSO for stage 05
       includes complete metadata timeline and frame-index sequences
```

Hard contract:

- Realtime stream and MP4 must come from the same encoded access-unit sequence.
- Stage 04 production code should not directly read mutable `Current*` SOs once a synchronized frame contract is available.
- Stage 04 must not create or republish the virtual layer. The virtual layer is produced in stage 01, buffered in stage 02, matched in stage 03, then consumed by stage 04.
- A missing MP4, missing realtime stream, or failed encoder must write explicit blocker text to the relevant state SOs.

## What Is Missing

- A mounted `TextureToAccessUnitEncoder` production component in `SampleScene`.
- Unity/Vulkan texture-to-encoder-surface implementation for real PCA/composite frames.
- `EncodedAccessUnitQueueSO` writer for H264/H265 access units.
- MP4 muxer that consumes the access-unit bus instead of recording through InstantReplay.
- MP4 muxer sample callbacks or access-unit-bus muxing that can make `mp4SampleIndex` a real muxer acknowledgement instead of a writer-side ordered index.
- Real access-unit encoder and muxer health reporting beyond the current `SingleEncodeHealthReporter` bridge.
- Device validation showing one Quest Android Player session producing both realtime packets and MP4 from the same access units.

## Stage Boundary Contract

`SingleEncodeStageBoundary` is the current public exit for stage 04.

Inputs:

- `MergedFrameSnapshotQueueSO`: stage-03 synchronized sendable frame queue.
- `MetadataTimelineJournalSO`: stage-03 complete metadata timeline.
- `EncodedAccessUnitQueueSO`, `Mp4ArtifactWriterStateSO`, `FrameIndexSO`, `EncodingHealthStateSO`: stage-04 internal products and health.

Output:

- `SingleEncodeOutputQueueSO` records include video artifact state, timestamp samples, full `MetadataTimelineEntryRecord[]`, and full `FrameIndexEntry[]`.
- Stage 05 should use the exported metadata timeline and frame-index arrays for completeness checks. The old direct reads from `MetadataTimelineJournalSO` / `FrameIndexSO` remain fallback paths, not the preferred 04 -> 05 contract.

## Recommended Next Steps

1. Keep `InstantReplayLocalMp4Recorder` as the local MP4 bootstrap path and use it to validate PCA texture readiness, orientation, file output, and stage-05 finalize gates.
2. Fix or explicitly document the `EncoderConfiguration.asset` mismatch before judging MP4 quality.
3. Implement `TextureToAccessUnitEncoder` against `Q3SurfaceVideoEncoder` and the native Vulkan bridge.
4. Replace the current MP4 writer with an access-unit bus consumer while preserving `Mp4ArtifactWriterStateSO` updates.
5. Add `EncodingHealth` and a real access-unit MP4 muxer, then keep `SingleEncodeStageBoundary` as the only public exit from stage 04.
