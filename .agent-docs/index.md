# Q3 Data Collection - Agent Documentation

> Project knowledge base for quick orientation.
> Last updated: 2026-06-13

## Quick Context

This Unity project captures Quest passthrough camera data, controller pose, CenterEye headset pose, and network/video metadata through ScriptableObjects under `Assets/SOData/DataCapture`. Passthrough camera data is split into image, timing, camera pose, camera metadata, and stream-state SOs so each part can be inspected and queued independently.

## Document Map

- [Q3 Data Collection Project Introduction (zh-CN)](q3-data-collection-project-introduction.zh-CN.md)
- [Data Capture Runtime Analysis (zh-CN)](data-capture-runtime-analysis.zh-CN.md)
- [Ideal Data Capture Chain And Current Gaps (zh-CN)](ideal-data-capture-chain-and-current-gaps.zh-CN.md)
- [DataCapture Clean Structure Plan (zh-CN)](data-capture-clean-structure-plan.zh-CN.md)
- [Current vs Ideal Data Capture Gaps (zh-CN)](current-vs-ideal-data-capture-gaps.zh-CN.md)
- [Quest APK Data Capture Debug Principles (zh-CN)](quest-apk-data-capture-debug-principles.zh-CN.md)
- [Stage 3 Time Alignment Upgrade Plan (zh-CN)](stage3-time-alignment-upgrade-plan.zh-CN.md)
- Scenes
  - [Scene And Data Flow](scenes/_flow.md)
  - [SampleScene](scenes/sample-scene.md)
- Assets
  - [DataCapture ScriptableObjects](assets/data-capture-scriptable-objects.md)
- Systems
  - [DataCapture Full Runtime Chain](systems/data-capture-full-chain.md)
  - [Encoding Layer Current State](systems/encoding-layer-current-state.md)
  - [Stage 04 Single Encode Production](systems/stage-04-single-encode-production.md)
  - [Stage 04 Encoding Flow Analysis (zh-CN)](systems/stage-04-encoding-flow-analysis.zh-CN.md)

## Runtime Flow

The active scene now groups the capture runtime under `DataCapture_Runtime` as `00_SessionControl`, `10_CurrentSOInputs`, `20_QueueBuffers`, `30_TimeSynchronization`, `40_SingleEncodeProduction`, `50_ProductAssembly`, `60_Distribution`, and `90_DebugAndTests`. Capture components write latest values into `Current*` ScriptableObjects, queue recorders persist valid samples while recording is active, `TimestampMerger` aligns the configured SO sequence, and the single-encode/product/distribution layers hold local MP4 and live-send wiring.

Current debug-chain design lives in `Assets/docs/10-so-debug-layer-design.md`. Older per-test SO-driven runner code and assets have been moved under `Legacy/SoDrivenTests` folders and the old scene test objects are inactive.

The SO debug implementation is under `Assets/DataCapture/Runtime/90_DebugAndTests/SOAccessAndPipeline` with layer folders matching the runtime scene chain. `90_IntegratedChain/DataCaptureSODebugPipeline.cs` is orchestration-only; network/recording, Current SO, queue, and synchronization checks live in their own layer files.
