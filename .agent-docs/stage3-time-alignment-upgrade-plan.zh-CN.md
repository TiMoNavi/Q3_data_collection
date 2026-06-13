# 阶段 3 同步时间驱动 MP4 PTS 升级路线

Last updated: 2026-06-13

## Stage 2 implementation update

2026-06-13:
- `VideoFrameInputResolver` now passes `MergedFrameSnapshotRecord.timestampUnixMs` explicitly into `CurrentVideoFrameInputSO.timestampUnixMs`.
- `CurrentVideoFrameInputSO` now preserves `sourceTimestampUnixMs` separately from the synchronized Stage 3 timestamp and records `synchronizedSnapshotFrameId`.
- `InstantReplayLocalMp4Recorder` now deduplicates and resolves metadata sidecar records by the Stage 3 snapshot id first, with source camera frame id as fallback.
- `LocalMp4EndToEndDebugRunner` now requires a non-legacy Stage 3 snapshot id in `LocalMp4E2E.04.Input` and prints the source/synchronized timestamp fields in logcat.
- Scope remains limited to the Stage 3 -> CurrentVideoFrameInput contract. Vulkan/GPU rendering and device execution are intentionally left to the separate test lane.

## Stage 2 device validation update

2026-06-13:
- APK tested: `Builds/Q3DataCollection_autotest_stage2_20260613_085314.apk`.
- Artifact directory: `captures/device_debug_20260613_0918_stage2_contract`.
- Pull result: `q3dc_local_mp4_20260613_011832.mp4`, `.manifest.json`, and `.metadata.jsonl`.
- Counts passed: manifest `frameCount=176`, metadata lines `176`, MP4 video packets `176`.
- Timebase passed: `timebaseSource=Stage3MergedFrameSnapshot.timestampUnixMs`, `mp4PtsTimebase=secondsSinceTimestampStartUnixMs`, `preserveInputFrameTimestamps=true`.
- PTS alignment passed: max normalized per-frame error `1.5 ms`, mean `1.5 ms`.
- Stage 3 identity passed in pulled metadata: `MergedFrameSnapshotRecord.frameId` is strictly increasing, camera image frame id is strictly increasing, and both sequences match for this run (`19 -> 256`).
- APK string verification passed: the built APK contains `synchronizedSnapshotFrameId`, `sourceTimestampUnixMs`, and the legacy-fallback blocker string.
- Logcat gate visibility is still open: `stage2_full_logcat.txt` contains no `SO-Debug` / `LocalMp4E2E` matches, so PASS is product-level rather than logcat-visible.

## 当前实施状态

2026-06-13 更新：

- Step 1 已落地：`InstantReplayLocalMp4Recorder` 已使用 `CurrentVideoFrameInputSO.timestampUnixMs` 推导 MP4 presentation seconds，并保留输入帧时间戳。
- Step 2 已做最小契约固化：`CurrentVideoFrameInputSO.timestampUnixMs` 已标注为阶段 3 同步时间。
- Step 3 已落地第一版自动 gate：`LocalMp4EndToEndDebugRunner` 新增 `LocalMp4E2E.06.TimeAlignment`。
- Step 4 已落地第一版 schema：本地 MP4 `.manifest.json` 新增 `timebaseSource`、`mp4PtsTimebase`、`preserveInputFrameTimestamps` 和 `minimumInitialPresentationSeconds`。
- 待下一轮 Android APK 设备验证：确认 logcat 出现 `LocalMp4E2E.06.TimeAlignment` PASS，并拉取新产物做一次 ffprobe 抽样复核。

## 目标

把本地 MP4 的视频时间轴和 metadata sidecar 统一到同一个时间源：`30_TimeSynchronization` 阶段输出的 `MergedFrameSnapshotRecord.timestampUnixMs`。

升级完成后，任意一帧的离线对齐公式应成立：

```text
mp4.pts_time ~= (metadata.timestampUnixMs - manifest.timestampStartUnixMs) / 1000.0
```

其中 `metadata.timestampUnixMs` 必须来自阶段 3 同步输出，而不是 recorder 自己采样的 Unity 当前时间。

## 当前事实

已验证本地视频采集链路跑通：

- `LocalMp4E2E.04.MP4Finalize` PASS。
- `LocalMp4E2E.05` PASS。
- `LocalMp4E2E.Pipeline` PASS。
- MP4 video packet 数、metadata 行数、manifest `frameCount` 均为 169。
- metadata 时间跨度约 `8250 ms`，MP4 视频 duration 约 `8.2511 s`，整体窗口对齐。

但逐帧时间戳仍存在偏差。原因是 `InstantReplayLocalMp4Recorder` 当前把 `Time.unscaledTimeAsDouble` 作为 `IFrameProvider.Frame` 的 timestamp 传给 MP4 encoder，而 metadata 写的是 `CurrentVideoFrameInputSO.timestampUnixMs`。

## 时间契约

### Canonical Time

唯一可信同步时间：

```text
MergedFrameSnapshotRecord.timestampUnixMs
```

该值由 `TimestampMerger` 使用 camera timing 的 `timestampUnixMs` 作为 target time 生成，并用于对齐 camera image、camera pose、camera metadata、stream state、controller、virtual layer 等输入。

### Current Video Input Time

`CurrentVideoFrameInputSO.timestampUnixMs` 必须表达阶段 3 同步时间，而不是 raw camera image fallback 时间，也不是 recorder 推帧时间。

短期允许 `sourceFrame.timestampUnixMs == snapshot.timestampUnixMs` 时直接沿用。长期应在 API 层明确传递 `synchronizedTimestampUnixMs`，避免 future source kind 或 fallback 改动破坏契约。

### MP4 Time

MP4 不写绝对 Unix time。MP4 视频轨写相对 PTS：

```text
presentationSeconds = (currentVideoFrameInput.timestampUnixMs - firstInputTimestampUnixMs) / 1000.0
```

`firstInputTimestampUnixMs` 写入 manifest 作为 `timestampStartUnixMs`。

### Metadata Time

metadata sidecar 继续写绝对 Unix ms。每行 metadata 必须对应同 index 的 MP4 video packet / frame。

## 改动范围

### 必改

1. `Assets/DataCapture/Runtime/40_SingleEncodeProduction/EncoderBackends/InstantReplayLocalPrototype/InstantReplayLocalMp4Recorder.cs`

   改动目的：MP4 PTS 改为使用阶段 3 同步时间的相对秒数。

   关键改动：

   - 增加 `firstInputTimestampUnixMs` 或复用现有 `firstPushedTimestampUnixMs` 前置计算。
   - `PushCurrentInputFrame(...)` 不再接收 `Time.unscaledTimeAsDouble` 作为视频 PTS。
   - `frameProvider.Push(...)` 使用 `ResolvePresentationSeconds(currentVideoFrameInput.timestampUnixMs)`。
   - 对重复 frame、倒退 timestamp、无效 timestamp 给出明确 failure/waiting 状态。

2. `Assets/DataCapture/Runtime/40_SingleEncodeProduction/SynchronizedFrameReader/VideoFrameInputResolver.cs`

   改动目的：把阶段 3 的同步时间显式传到 `CurrentVideoFrameInputSO`。

   短期最小改法：

   - `TryGetSynchronizedSource(...)` 返回 `MergedFrameSnapshotRecord snapshot` 或返回 `long synchronizedTimestampUnixMs`。
   - 写入 `CurrentVideoFrameInputSO` 时使用 `snapshot.timestampUnixMs`。

   长期干净改法：

   - `CurrentVideoFrameInputSO.SetFrame(...)` 增加 `synchronizedTimestampUnixMs` 参数。
   - 保留 `sourceCameraFrameId` 表示源帧 ID。
   - `timestampUnixMs` 明确为同步时间。

3. `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/40_SingleEncodeProduction/Transition/CurrentVideoFrameInputSO.cs`

   改动目的：把字段语义写硬，避免后续误用。

   建议：

   - 注释或字段命名说明 `timestampUnixMs` 是 synchronized timestamp。
   - 如需兼容，新增 `sourceTimestampUnixMs`，但不要在第一轮扩大范围。

4. `Assets/DataCapture/Runtime/90_DebugAndTests/SOAccessAndPipeline/90_IntegratedChain/LocalMp4EndToEndDebugRunner.cs`

   改动目的：增加时间对齐门控。

   新增 gate：

   ```text
   [SO-Debug][PASS][LocalMp4E2E.06.TimeAlignment]
   ```

   检查内容：

   - metadata 行数 == MP4 video packet/frame 数。
   - manifest `frameCount` == metadata 行数。
   - MP4 duration 和 metadata 首尾跨度差值 <= 1 frame 或 <= 50 ms。
   - 抽样 index 的 PTS 差值 <= 1 frame；目标值 <= 5 ms。

### 可选

1. `Assets/DataCapture/Runtime/50_ProductAssembly/...`

   若产品组装层已经使用 metadata/frame index 的 timestamp，不需要第一轮改动。只在发现它读取 MP4 PTS 作为主时间源时再调整。

2. `Assets/DataCapture/Runtime/40_SingleEncodeProduction/StageBoundary/SingleEncodeStageBoundary.cs`

   可追加字段检查，例如 `timebaseSource`、`ptsAligned=True`。不要在第一轮扩大到结构性重构。

3. manifest schema

   可新增：

   ```json
   {
     "timebaseSource": "Stage3MergedFrameSnapshot.timestampUnixMs",
     "mp4PtsTimebase": "secondsSinceTimestampStartUnixMs",
     "timeAlignmentMaxDeltaMs": 5
   }
   ```

### 不在本次范围

- 手柄是否有有效 pose 数据。当前 169 条 controller position/rotation 都是零值或 identity，这是输入源可用性问题，不影响 MP4/metadata 时间轴升级。
- 头显是否有明显运动。当前视角几乎不动，不适合作为时间同步判断依据。
- PC Receiver、网络发送、Debug JPEG 链路。
- 音频轨质量。当前 MP4 有 0 秒 AAC track 警告，可以后续清理；本次只处理 video PTS 与 metadata 对齐。
- 大规模重构阶段 3 同步算法。

## 推进顺序

### Step 0：锁定基线

目标：保留当前 PASS 产物作为对照。

输入：

```text
captures/device_debug_20260613_044537_stage05_fix_gate_run/
```

记录：

- 当前 MP4 video packets = 169。
- 当前 metadata lines = 169。
- 当前 window duration 差约 1.1 ms。
- 当前逐帧 PTS 最大偏差约 186 ms。

产出：

- 一份基线对齐结果，作为升级前对照。

### Step 1：最小闭环改 MP4 PTS

目标：只改 `InstantReplayLocalMp4Recorder`，让 MP4 PTS 使用 `CurrentVideoFrameInputSO.timestampUnixMs`。

实现要点：

```csharp
private long firstInputTimestampUnixMs;

private double ResolvePresentationSeconds(long timestampUnixMs)
{
    if (timestampUnixMs <= 0)
    {
        return 0.0;
    }

    if (firstInputTimestampUnixMs <= 0)
    {
        firstInputTimestampUnixMs = timestampUnixMs;
    }

    return Math.Max(0L, timestampUnixMs - firstInputTimestampUnixMs) / 1000.0;
}
```

验收：

- APK 可构建。
- `LocalMp4E2E.Pipeline` 仍 PASS。
- MP4 video packet 数 == metadata 行数。
- `mp4.pts_time` 与 `metadata.timestampUnixMs - manifest.timestampStartUnixMs` 的抽样差值明显下降。

风险：

- 输入 timestamp 重复或倒退会导致 MP4 encoder 报错或生成非单调 PTS。

缓解：

- 保留 `sourceCameraFrameId == lastPushedSourceFrameId` 的重复帧拦截。
- 新增 timestamp 单调检查。
- 出现倒退时 fail fast，日志包含 sourceFrameId、timestamp、lastTimestamp。

### Step 2：把阶段 3 时间显式传到 CurrentVideoFrameInput

目标：消除“当前刚好相等”的隐性假设。

实现要点：

- `VideoFrameInputResolver` 从 `MergedFrameSnapshotRecord` 读取 `snapshot.timestampUnixMs`。
- `CurrentVideoFrameInputSO.SetFrame(...)` 接收并写入该同步时间。
- `CurrentVideoFrameInputSO.timestampUnixMs` 文档化为 synchronized timestamp。

验收：

- `LocalMp4E2E.04.Input` 日志里 `input.timestampUnixMs` 与同步层 latest sendable snapshot timestamp 一致。
- metadata sidecar 每行 `timestampUnixMs` 与对应 snapshot 时间一致。

### Step 3：增加 `LocalMp4E2E.06.TimeAlignment`

目标：让 APK 自动门控发现时间漂移，不依赖人工 ffprobe 对比。

实现建议：

- Debug runner 在 finalize 后读取本地 manifest 和 metadata sidecar。
- 如运行时无法直接读 MP4 packet PTS，可第一版只检查：
  - metadata 行数。
  - manifest frameCount。
  - manifest 首尾时间跨度。
  - writer sampleCount。
- 完整版再加入本地或设备侧 MP4 PTS probe。

PASS 日志格式：

```text
[SO-Debug][PASS][LocalMp4E2E.06.TimeAlignment] fields=frames=169; metadataSpanMs=8250; mp4DurationMs=8251; maxSampleDeltaMs=...
```

FAIL 日志必须包含：

```text
target=<metadata|mp4|manifest>
condition=<expected alignment>
actual=<observed delta/count>
blocker=<reason>
```

验收：

- gate 自动输出 `LocalMp4E2E.06.TimeAlignment`。
- 失败时 ADB/logcat 可直接看出是 frame count、duration、PTS sample 哪一项不对齐。

### Step 4：产物 schema 和文档收束

目标：让离线消费方知道如何解释时间。

更新：

- manifest 增加 timebase 字段。
- `.agent-docs/current-vs-ideal-data-capture-gaps.zh-CN.md` 更新“时间对齐已升级/待验证”状态。
- `.agent-docs/quest-apk-data-capture-debug-principles.zh-CN.md` 只保留调试原则，不加入复杂实现细节。

验收：

- 新产物自描述时间来源。
- 离线脚本只需要 manifest + metadata + MP4，就能建立帧时间映射。

## 最终验收标准

一轮 Android APK 自动 gate 通过时，必须满足：

- `LocalMp4E2E.Start` PASS。
- `LocalMp4E2E.04.MP4Writer` PASS。
- `LocalMp4E2E.04.MP4Finalize` PASS。
- `LocalMp4E2E.05` PASS。
- `LocalMp4E2E.06.TimeAlignment` PASS。
- `LocalMp4E2E.Pipeline` PASS。
- 无 `SCREEN_OFF`、`APP_CMD_STOP`、`OnApplicationPause(true)` 干扰。
- metadata 行数 == MP4 video packet/frame 数 == manifest `frameCount`。
- metadata 首尾时间跨度与 MP4 video duration 差值 <= 1 frame。
- 抽样 PTS 对齐误差 <= 1 frame，目标 <= 5 ms。

## 推荐执行节奏

第一轮只做 Step 1，快速验证 MP4 PTS 是否能被 InstantReplay 正确采用。

第二轮做 Step 2，把时间契约写硬。

第三轮做 Step 3，把时间对齐纳入自动 gate。

第四轮做 Step 4，补 schema 和文档状态。

不要把音频轨清理、controller 数据有效性、PC Receiver、网络发送一起塞进本次升级。先让本地 MP4 + metadata 的时间线稳定下来。
