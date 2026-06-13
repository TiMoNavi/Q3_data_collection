# 当前链路距离理想数据采集流程的差距

Last updated: 2026-06-12

## 结论

现在已经完成的是“结构对齐”：

- 代码目录、SO 类型目录、SOData 实例目录已经按 `00 -> 60 -> 90` 分层。
- `SampleScene.unity` 里的 `DataCapture_Runtime` 已经重排成理想八段式层级。
- 缺失的关键 SO 类型和 `.asset` 实例已经补齐。

但现在还没有完成的是“运行时契约对齐”。场景和资源已经长得像理想流程，但很多新阶段仍是空节点、状态 SO 或队列 SO 没有正式 writer，编码层仍存在读取可变 Current 状态的过渡路径。

理想流程的硬约束应该是：

```text
10_CurrentSOInputs
  -> 20_QueueBuffers
  -> 30_TimeSynchronization
  -> stable MergedFrameSnapshotRecord(frameId, sourceTimestamp, videoInputRef, metadata group)
  -> 40_SingleEncodeProduction
  -> EncodedAccessUnitRecord(frameId, sourceTimestamp, encodedPts, sample bytes)
  -> 50_ProductAssembly
  -> 60_Distribution
```

现在最大差距集中在后三段：

```text
30_TimeSynchronization 的 MergedFrameSnapshotQueueSO 已增强为 40 的唯一正式输入语义。
40_SingleEncodeProduction 现在有 SingleEncodeOutputQueueSO 作为统一外部出口，但真实 texture -> MediaCodec access unit bus 仍未打通。
50_ProductAssembly 已有并已挂载 SingleEncodeOutputProductBuilder 的入口适配器，但还需要完善正式 artifact 文件产物。
60_Distribution 还没有只消费 50 的产物来做分发。
```

Meta PCA 事实前提：`PassthroughCameraAccess` 提供 live texture、timestamp、intrinsics、pose 等相机数据，但不是现成 H264/H265 编码流。真实视频流和 MP4 仍需要项目自己的编码生产线。

## 已经接近理想的部分

### 00 Session Control

当前已经有：

- `NetworkSenderConfiguration.asset`
- `OutputRouteGate.asset`
- `PCReceiverConnectionStatus.asset`
- `RecordingSessionState.asset`
- `RecordingToggleRequest.asset`
- `LanDiscoveryClient`
- `RecordingSessionController`
- `SessionModeController`
- `OutputRouteGateController`

差距：

- `00_ModeSelection` 已挂载 `SessionModeController`，显式读取 `SessionMode.asset` 并同步 `NetworkSenderConfiguration.asset.outputTarget`。
- 当前 `SessionMode.asset` 为 `LocalOnly / LocalFile`，`OutputRouteGate.asset` 已刷新为不要求网络握手且允许开始录制。
- `OutputRouteGate` 和后续 `DistributionGateState` 的职责仍需在后续阶段拆清：前者决定能否开始录制，后者决定产物能否发布或发送。

理想状态：

```text
00_ModeSelection 明确暴露当前 session mode。
10_NetworkHandshakeIfNeeded 只在网络模式下影响 CanStartRecording。
30_RecordingState 只负责 session 生命周期和 ShouldWriteQueues。
40_OutputRouteGate 只回答“现在能不能开始录制”。
```

### 10 Current SO Inputs

当前已经有：

- PCA current writer
- virtual layer current writer
- controller current writer
- headset current writer
- coordinate calibration
- network device placeholder

差距：

- Virtual layer 虽然有 Current/Queue，但它是否属于正式同步输入还没有在 alignment 配置和下游契约里完全确定。
- `CurrentVideoFrameInputSO` 仍作为编码层的重要过渡输入存在，容易绕过 `Current -> Queue -> Sync` 的稳定帧契约。
- 理想的 `videoInputRef` 应该由 `MergedFrameSnapshotRecord` 固定下来，而不是编码时临时读取一个可能已经变化的 Current texture。

理想状态：

```text
10 只写 Current。
10 不直接喂编码器。
任何会进入视频编码的画面输入，都必须先被 20/30 纳入可追溯的 frameId/sourceTimestamp。
```

### 20 Queue Buffers

当前已经有：

- camera 五路 queue recorder
- virtual layer queue recorder
- controller queue recorder
- headset queue recorder
- network device optional recorder

差距：

- `00_PassthroughCamera_Queues` 现在一个 GameObject 上挂了 5 个 `CurrentToQueueRecorder`，功能可用但 Inspector 可读性不够好。
- 停止录制和 warmup 完成时会清队列，对现场调试和失败复盘不友好。
- optional stream 的接入策略还没有形成统一规则：哪些 optional 缺失只降级，哪些 required 缺失必须阻断。

理想状态：

```text
每条 Current -> Queue 的关系在 Inspector 里一眼可见。
调试模式下能保留最近一次 session 的队列快照。
required/optional stream 策略只由 30_TimeSynchronization 判定。
```

## 关键未完成部分

### 30 Time Synchronization

当前已经有：

- `TimestampMerger`
- `MergedFrameSnapshotQueue.asset`
- `TimestampMergerDebugState.asset`
- `MetadataTimelineJournal.asset`
- `SynchronizationHealthState.asset`

差距：

- `10_MetadataTimelineJournal` 场景节点目前是空节点，缺少正式 writer。
- `20_SynchronizationHealth` 场景节点目前是空节点，缺少 health reporter。
- `MergedFrameSnapshotQueueSO` 已提供 `TryGetLatest`、`TryGetLatestSendable`、`ExportSendableSnapshot`，用于把 03 出口明确成 04 的统一入口。
- 当前 metadata sender 可以直接读 `MergedFrameSnapshotQueue`，但编码层仍可能读 `CurrentVideoFrameInputSO`，这会造成 metadata/video 错位风险。

理想状态：

```text
TimestampMerger 输出的每个 MergedFrameSnapshotRecord 都包含：
  frameId
  sourceTimestamp
  required/optional stream 对齐结果
  videoInputRef 或可重建 video input 的稳定引用
  drop/sendable reason

MetadataTimelineJournal 记录完整同步后 metadata 时间线。
SynchronizationHealthState 明确说明最新帧是否可进入 40。
```

### 40 Single Encode Production

这是当前离理想最远的一层。

当前已经有：

- `VideoFrameInputResolver`
- `PassthroughCameraLayerCompositor`
- `InstantReplayLocalMp4Recorder`
- `EncodedAccessUnitQueue.asset`
- `CurrentEncodedAccessUnit.asset`
- `EncodingHealthState.asset`
- `Mp4ArtifactWriterState.asset`
- `FrameIndex.asset`
- `SingleEncodeOutputQueueSO`
- `SingleEncodeStageBoundary`
- Android MediaCodec smoke / pattern 路径

差距：

- `00_SynchronizedFrameReader` 现在实际挂的是 `VideoFrameInputResolver`，它仍围绕 `CurrentVideoFrameInputSO` 工作。
- `20_TextureToAccessUnitEncoder` 是空节点，还没有正式的 Unity/PCA texture -> MediaCodec access unit encoder。
- `30_Mp4MuxerOrVideoArtifactWriter` 当前是 `InstantReplayLocalMp4Recorder`，能做本地 MP4 原型，但不是最终 single encode bus 的 muxer sink；它有 `androidPlayerOnly = true`，所以本地 MP4 应视为 Android Player / Quest 设备能力，不是 Unity Editor 能力。
- `InstantReplayLocalMp4Recorder` 当前仍会通过旧 `EncodedOutputMetadataBinder -> CaptureOutputQueueSO` 发布 file artifact，同时已经写入 `Mp4ArtifactWriterStateSO`，让 04 的 `SingleEncodeStageBoundary` 和 05 的 `SessionArtifactManifestBuilder` 能看到本地 MP4 的开始、完成、失败或阻塞状态。
- `40_FrameIndexWriter` 是空节点，缺少 MP4 sample / encoded access unit / metadata record 的映射写入。
- `50_EncodingHealth` 是空节点，缺少对编码失败、丢帧、PTS 异常、muxer 异常的正式场景挂载。
- `SingleEncodeStageBoundary` 已挂到 `SampleScene` 的 `40_SingleEncodeProduction/60_StageBoundary`，会把内部 `EncodedAccessUnitQueue`、`Mp4ArtifactWriterState`、`FrameIndex`、`EncodingHealthState` 聚合为 04 统一出口。
- Android `Q3SurfaceVideoEncoder` 的 synthetic pattern + muxer 路径证明了 MediaCodec/MediaMuxer 可用，但真实 Unity/PCA/composite texture 写入 encoder surface 还没打通。
- 网络实时 H264/H265 视频流还没通，不能把现状描述成“实时视频流已经可用”。

理想状态：

```text
40.1 SynchronizedFrameReader
  只读 MergedFrameSnapshotRecord

40.2 SingleRenderTextureBuilder
  按 snapshot 指定的画面输入构建 raw / virtual / composite texture

40.3 TextureToAccessUnitEncoder
  单次编码，输出 EncodedAccessUnitRecord

40.4 EncodedAccessUnitQueue
  同一批 access units 同时喂给 realtime packetizer 和 MP4 muxer

40.5 Mp4MuxerOrVideoArtifactWriter
  只作为 access unit sink，不再独立重新取 Current 录制

40.6 FrameIndexWriter
  记录 frameId/sourceTimestamp/accessUnit/MP4 sample/metadata 的映射

40.7 EncodingHealthState
  编码失败会阻止实时发送和最终 artifact 正常发布

40.8 SingleEncodeStageBoundary
  04 的唯一外部出口。它读取 03 的 MergedFrameSnapshotQueueSO 作为统一入口，
  聚合 04 内部编码产物，并发布 SingleEncodeOutputQueueSO 给 05。
```

核心验收标准：

```text
禁止正式编码路径直接读取会继续变化的 CurrentVideoFrameInputSO。
实时视频流和本地 MP4 必须来自同一批 EncodedAccessUnitRecord。
```

### 50 Product Assembly

当前已经有：

- `RealtimeAlignedStreamQueue.asset`
- `SessionArtifactManifest.asset`
- `SessionFinalizeState.asset`
- `CaptureOutputQueue.asset`
- `EncodedOutputMetadataBinder`
- `SingleEncodeOutputProductBuilder`
- `RealtimeAlignedStreamQueueBuilder`
- `SessionArtifactManifestBuilder`
- `SessionFinalizeController`

差距：

- `00_RealtimeAlignedStreamQueueBuilder` 已挂到 `SampleScene`，当前从 `EncodedAccessUnitQueue.asset` 和 `MetadataTimelineJournal.asset` 增量生成 `RealtimeAlignedStreamQueue.asset`；只有当前 `outputTarget` 需要网络输出时才工作，作为录制中的流式传输产物。
- `10_SessionArtifactManifestBuilder` 已挂到 `SampleScene`，当前从 `Mp4ArtifactWriterState.asset`、`MetadataTimelineJournal.asset`、`FrameIndex.asset`、`EncodedAccessUnitQueue.asset` 生成 `SessionArtifactManifest.asset`；只有当前 `outputTarget` 需要本地文件时才把 MP4 视为必需产物。
- `20_SessionFinalizeController` 已挂到 `SampleScene`，录制从 active 状态回到 `NotStarted` 时按 `NetworkSenderConfiguration.outputTarget` 评估 publish/quarantine：`LocalFile` 只要求 MP4，`RemoteReceiver/SelfReceiver` 只要求 realtime stream，`RemoteAndLocalFile/SelfAndLocalFile` 两类产物都要求；缺失项会写入 `SessionFinalizeState.asset` 的 blocker 字段。
- `SingleEncodeOutputProductBuilder` 仍保留为兼容桥，可从 `SingleEncodeOutputQueueSO` 构建 50 产物，但当前三节点实现已经能分别覆盖实时流式产物和结束后的 MP4/manifest 产物。
- `CaptureOutputQueue` 语义过宽，不能同时精确表达 realtime aligned stream、complete MP4、metadata timeline、frame index。
- 缺少完整 metadata timeline 文件产物。
- 缺少 frame index 产物，录制结束后无法可靠说明 metadata record 和 MP4 sample 的映射。

理想状态：

```text
RealtimeAlignedStreamQueue
  encoded video packet
  + synchronized metadata
  + frameId/sourceTimestamp/encodedPts

SessionArtifactManifest
  complete MP4
  + complete metadata timeline
  + frame index
  + session mode / calibration / encoder config

SessionFinalizeState
  publish / discard / quarantine
  + reason
```

### 60 Distribution

当前已经有：

- `NetworkTransmissionCoordinator`
- `MetadataPacketSender`
- `VideoPacketSender`
- UDP metadata/video transports
- `NetworkSenderConfiguration.asset`
- `DistributionGateState.asset`
- `LocalSessionArtifactStoreState.asset`

差距：

- `00_RoutePolicy` 是空节点，还没有统一消费 `SessionMode` / finalize state / output policy。
- `10_LocalSessionArtifactStore` 是空节点，缺少正式本地 artifact 存储器。
- `20_LiveNetworkStreamSender` 里目前主要是旧 metadata/video sender 和 coordinator 组合，还不是只消费 `RealtimeAlignedStreamQueue` 的统一 sender。
- `30_SessionArtifactFileTransfer` 是空节点，缺少录制结束后的完整 artifact 传输链路。
- 当前网络主链路更偏向 metadata 发送，真实 H264/H265 access units 尚未从 40/50 推到 PC receiver。
- `60_Distribution` 还没有完全做到“只分发，不触发第二次渲染或第二次编码”。

理想状态：

```text
LocalOnly:
  SessionArtifactManifest -> LocalSessionArtifactStore

NetworkOrHybrid during recording:
  RealtimeAlignedStreamQueue -> LiveNetworkStreamSender -> PCReceiver

NetworkOrHybrid after stop:
  SessionArtifactManifest -> SessionArtifactFileTransfer -> PCReceiver
```

## 横向差距

### 配置语义还不完全统一

当前存在容易混淆的配置：

- `EncodingPipelineConfiguration.asset` 倾向 LocalMp4 / AndroidMediaCodecH264。
- `EncoderConfiguration.asset` 仍可能保留 DEBUG_JPEG、低帧率、低分辨率语义。
- `CaptureTransmissionGateSO` 既像发送门控，又容易被误解成启动录制门控。

需要统一成：

```text
SessionMode / OutputRoute: 决定能否开始和发到哪里。
EncodingPipelineConfiguration: 决定使用哪条编码生产线。
EncoderConfiguration: 决定 codec、分辨率、fps、bitrate 等编码参数。
EncodingHealthState: 决定编码产物是否可信。
DistributionGateState: 决定 50 的产物是否允许被 60 分发。
```

### 运行时 SO 会污染 asset 状态

大量 Current/Queue/Debug/State SO 都是 asset。Play Mode、调试工具、外部 SO write request 都可能改它们，导致：

- scene/project dirty 噪声。
- 上一次运行残留影响下一次判断。
- 版本控制里出现运行时状态变化。

需要决定：

```text
哪些 SO 是配置资产。
哪些 SO 是运行时状态资产但需要自动 reset。
哪些 SO 应该迁移到 play-session runtime instance。
```

### 调试节点还需要更明确的边界

当前 `90_DebugAndTests` 已经隔离出来，但仍要注意：

- smoke runner 只能证明某个技术点，不代表生产链路已通。
- Debug JPEG 只能作为 probe，不能作为正式编码输出。
- SO debug probe 不应成为生产链路必经节点。

## 建议优先级

### P0: 先把稳定帧契约打穿

目标：

```text
30_TimeSynchronization
  -> MergedFrameSnapshotRecord
  -> 40_SingleEncodeProduction
```

任务：

- 保持 `MergedFrameSnapshotQueueSO` 作为 04 的唯一外部输入，不新增 03->04 中转队列。
- 实现/挂载 `MetadataTimelineJournalWriter`。
- 实现 `SynchronizationHealthReporter`。
- 明确 `MergedFrameSnapshotRecord` 里的 video input identity。
- 改造 40 的正式入口，让正式路径只读 snapshot，不直接读 mutable Current。

### P1: 做 single encode access unit bus

目标：

```text
TextureToAccessUnitEncoder
  -> EncodedAccessUnitQueue
  -> realtime sink + MP4 sink
```

任务：

- 完成真实 Unity/PCA/composite texture -> MediaCodec input surface。
- 让 encoder 输出 `EncodedAccessUnitRecord`。
- MP4 muxer 和 realtime packetizer 读同一批 access units。
- 写入 `EncodingHealthState`。

### P2: 补齐最终产物组装

目标：

```text
SingleEncodeOutputQueue
  -> RealtimeAlignedStreamQueue
  -> SessionArtifactManifest
```

任务：

- 挂载 `SingleEncodeOutputProductBuilder`。
- 让 05 只读 `SingleEncodeOutputQueueSO`，不要直接读 `EncodedAccessUnitQueue`、`Mp4ArtifactWriterState`、`FrameIndex` 等 04 内部细节。
- 补齐正式 `FrameIndexWriter` 和 manifest 文件输出。

### P3: 让分发层只消费产物

目标：

```text
60_Distribution 不再回头读 Current、Queue 或触发编码。
```

任务：

- 实现 `DistributionRoutePolicy`。
- 实现 `LocalSessionArtifactStore`。
- 改造 `LiveNetworkStreamSender` 只读 `RealtimeAlignedStreamQueue`。
- 实现 `SessionArtifactFileTransfer`。

## 最终验收问题

当这套流程达到理想状态时，应该能直接回答：

1. 当前 session 是 LocalOnly 还是 NetworkOrHybrid？
2. 网络模式下 PC handshake 是否满足开始录制条件？
3. 正在录制时哪些 Current 被写入哪些 Queue？
4. 哪些 required stream 缺失会阻止同步输出？
5. 每个可编码帧的 `frameId/sourceTimestamp` 是在哪里固定的？
6. 编码器是否完全避免读取会变化的 Current 状态？
7. 实时流和 MP4 是否来自同一批 `EncodedAccessUnitRecord`？
8. MP4 sample 和 metadata record 的映射在哪里保存？
9. 录制结束后 manifest 是否能证明产物完整？
10. `60_Distribution` 是否只做分发，不做第二次渲染或编码？

现在这些问题里，前 1-4 个已经有较清晰的结构基础；第 5-10 个还需要运行时实现继续补齐。
