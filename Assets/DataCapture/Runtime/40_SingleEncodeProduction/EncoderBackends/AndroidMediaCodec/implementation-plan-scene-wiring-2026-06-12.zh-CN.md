# Single Encode 推进规划与场景接线审计

Last updated: 2026-06-12

## 目的

这份文档用于指导下一阶段实现，不替代 `README.zh-CN.md` 的架构原则。

当前目标仍然是：

```text
PCA / Unity composite RenderTexture
  -> one MediaCodec H264/H265 encode
  -> one access unit bus
       -> realtime video stream
       -> MediaMuxer MP4
       -> CaptureOutputQueueSO
       -> local / network / cloud artifact sinks
```

本轮重点是先看清 Unity 场景内已有接线，尤其是 `SampleScene.unity` 的 `DataCapture_Runtime`。结论是：`SampleScene` 里确实有一套较完整的正式接线骨架，但它仍停留在 Debug JPEG / InstantReplay Local MP4 / old UDP packet sender 阶段，不能直接代表最终 Single Encode 架构。

## 已核对前提

Meta 当前 PCA 文档仍指向 Unity `PassthroughCameraAccess` 作为 live camera texture 的上游入口。项目内实机也已经证明：

```text
PassthroughCameraAccess.GetTexture()
  -> staging RenderTexture
  -> InstantReplay MP4
```

所以现在主要风险不是 PCA 取图，而是：

```text
Unity VkImage
  -> MediaCodec input Surface pixels
  -> H264/H265 access units
```

## 当前 active scene

Unity MCP 读取到当前 active scene：

```text
Assets/Scenes/SampleScene.unity
```

root objects:

```text
Directional Light
Global Volume
[BuildingBlock] Camera Rig
[BuildingBlock] Passthrough
[BuildingBlock] Passthrough Camera Access
DataCapture_Runtime
```

`[BuildingBlock] Passthrough Camera Access` 在场景中存在；PCA runtime writer 不挂在该 building block 上，而是在 `DataCapture_Runtime/10_CurrentSOInputs` 下。

## SampleScene / DataCapture_Runtime 当前接线

### 00_Handshake_RecordingControl

本地 / 网络模式已经在握手之前增加独立输出路由门控：

```text
OutputRouteGate
  OutputRouteGateController
    networkConfiguration -> NetworkSenderConfiguration.asset
    pcReceiverStatus -> PCReceiverConnectionStatus.asset
    discoveryRequest -> PCDiscoveryRequest.asset
    outputRouteGate -> OutputRouteGate.asset
    handshakeStage -> LanDiscoveryClient
```

运行规则：

```text
LocalFile
  -> OutputRouteGateSO.requiresNetworkHandshake = false
  -> disable LanDiscoveryClient
  -> clear pending PCDiscoveryRequest
  -> allow RecordingSessionController

network output target
  -> OutputRouteGateSO.requiresNetworkHandshake = true
  -> enable LanDiscoveryClient
  -> wait for PCReceiverConnectionStatusSO.CanStartRecording
  -> then allow RecordingSessionController
```

`LanDiscoveryClient` 已移除 `UsesNetwork` 判断，只保留 discovery / response / pairing 职责。`RecordingSessionController` 也不再直接依赖 `NetworkSenderConfigurationSO` 和 `PCReceiverConnectionStatusSO`，只读取 `OutputRouteGateSO`。

对应 SOData：

```text
Assets/SOData/DataCapture/00_SessionControl/OutputRouteGate.asset
```

### 10_CurrentSOInputs

```text
DataCapture_Runtime/10_CurrentSOInputs/20_Camera_Passthrough_CurrentWriter
  PassthroughCameraFrameWriter
    cameraAccess -> [BuildingBlock] Passthrough Camera Access
    currentImage -> CurrentCameraImage.asset
    currentTiming -> CurrentCameraFrameTiming.asset
    currentCameraPose -> CurrentCameraPose.asset
    currentMetadata -> CurrentCameraMetadata.asset
    currentStreamState -> CurrentCameraStreamState.asset
```

这部分是可复用的正式上游。它负责把 PCA texture、timestamp、pose、intrinsics、stream state 写入 split current SO。

### 50_EncodingNetwork

当前子节点：

```text
10_SharedVideoFrameInput
15_PassthroughUnityCompositor
20_LocalMp4Save
25_EncodedOutput
30_DebugLowFpsImage
```

#### 10_SharedVideoFrameInput

```text
VideoFrameInputResolver
  inputConfiguration -> VideoFrameInputConfiguration.asset
  currentVideoFrameInput -> CurrentVideoFrameInput.asset
  currentCameraImage -> CurrentCameraImage.asset
  currentStreamState -> CurrentCameraStreamState.asset
  encoderConfiguration -> EncoderConfiguration.asset
  pipelineConfiguration -> EncodingPipelineConfiguration.asset
  compositor -> 15_PassthroughUnityCompositor
  resolveOnUpdate = true
  updateOnlyForNewSourceFrame = true
```

这是最终架构应该保留的统一输入边界。编码器应该从 `CurrentVideoFrameInputSO.inputTexture` 取输入，而不是自己找 PCA 或 compositor。

#### 15_PassthroughUnityCompositor

```text
PassthroughCameraLayerCompositor
  passthroughCameraAccess -> [BuildingBlock] Passthrough Camera Access
  compositedLayers = 0
  layerCamera = null
  compositeShader -> PassthroughAlphaComposite.shader
  renderOnlyWhenPassthroughUpdates = true
```

这部分是 composite 输入的原型骨架。当前 `compositedLayers = 0`，意味着即使 compositor 接线存在，也未必实际叠加任何 Unity overlay layer。后续验证 composite 时必须先明确 overlay layer mask 和测试对象。

#### 20_LocalMp4Save

```text
InstantReplayLocalMp4Recorder
  currentVideoFrameInput -> CurrentVideoFrameInput.asset
  recordingState -> RecordingSessionState.asset
  mergedSnapshotQueue -> MergedFrameSnapshotQueue.asset
  outputBinder -> 25_EncodedOutput
  followRecordingSessionState = true
  publishFileArtifact = true
```

这条路径可作为 “CurrentVideoFrameInputSO -> MP4 file artifact” 的历史原型，但它使用 InstantReplay，不是最终 Single Encode bus。

#### 25_EncodedOutput

```text
EncodedOutputMetadataBinder
  mergedSnapshotQueue -> MergedFrameSnapshotQueue.asset
  bindingConfiguration -> EncodedOutputBindingConfiguration.asset
  recordingState -> RecordingSessionState.asset
  currentOutput -> CurrentCaptureOutput.asset
  outputQueue -> CaptureOutputQueue.asset
```

这是可复用的输出 envelope / metadata binding 层。最终 access unit bus 应通过它或同等边界发布 `FramePacket` 和 `FileArtifact`。

#### 30_DebugLowFpsImage

```text
AsyncDebugJpegNetworkStreamer
  currentVideoFrameInput -> CurrentVideoFrameInput.asset
  outputBinder -> 25_EncodedOutput
  videoSender -> DataCapture_Runtime/50_NetworkSend/10_PacketSenders/VideoPacketSender
  publishCaptureOutput = true
  streamOnUpdate = false
  maxFramesPerSecond = 2
  maxDimension = 320
  jpegQuality = 70
```

这是 debug image path，不是最终视频流。它可以继续保留为低频视觉诊断。

### 50_NetworkSend

当前仍是旧 UDP packet sender：

```text
00_Transports
  UdpTransport_Metadata
  UdpTransport_Video

10_PacketSenders
  MetadataPacketSender
  VideoPacketSender

20_Coordination
```

这些组件可以作为传输配置和旧协议参考，但不是最终 `NetworkFramePacketSender / NetworkFileArtifactSender`。

### 40_EncodingDecode

当前是 placeholder / legacy：

```text
00_DebugImageEncoding_DEBUG_ONLY
10_VideoEncoding_PLACEHOLDER
  EncodedFrameQueueWriter_PLACEHOLDER_H264_H265
```

这里不应继续作为最终 H264/H265 实现入口。最终实现应放回 `50_EncodingNetwork` 的 Single Encode 输出模式下。

## 当前缺失

`SampleScene/DataCapture_Runtime` 里当前没有：

```text
SingleEncodeAccessUnitBus
ITextureVideoEncoder / AndroidSurfaceVideoEncoder
MediaCodec Unity texture bridge consumer
NetworkFramePacketSender
NetworkFileArtifactSender
LocalArtifactStore
```

`SingleEncodePcSmoke.unity` 里有 `UnityDynamicTextureEncoderSmokeRunner`，适合继续验证 dynamic RT bridge；但它不是正式 `DataCapture_Runtime` 接线。

## 推进原则

1. `SampleScene` 只做边界清晰的接线调整；输出路由 gate 已完成，暂不迁移整套 Single Encode bus。
2. 不先做 network / cloud sinks。
3. 不用 Java pattern fallback 冒充成功。
4. 先让 `UnityDynamicTextureEncoderSmokeRunner` 的动态 RT 真实进 MediaCodec。
5. 再接 `CurrentVideoFrameInputSO`。
6. 最后才整理 `SampleScene/DataCapture_Runtime` 正式接线。

## 执行顺序

### Phase 1: Dynamic RT bridge smoke

目标：

```text
UnityDynamicTextureEncoderSmokeRunner dynamic RenderTexture
  -> Q3SurfaceVideoEncoder.encodeUnityTextureFrame(...)
  -> native Vulkan bridge
  -> MediaCodec input Surface
  -> non-empty access units
  -> MediaMuxer MP4
```

工作项：

1. 在 `q3dc_vulkan_bridge.cpp` 中补最小 GPU 写入路径。
2. 在 `Q3SurfaceVideoEncoder.encodeUnityTextureFrame(...)` 中调用 native 写入并 drain。
3. 保持 `fallbackToPatternWhenBridgeUnavailable = false`。
4. 构建并上 Quest 验证。

验收：

```text
bridgeOutputFrameCount > 0
publishedFramePacketCount > 0
muxedSampleCount > 0
mp4Bytes > header-only size
MP4 extracted frame shows Unity dynamic test texture
```

### Phase 2: Raw PCA through CurrentVideoFrameInputSO

目标：

```text
CurrentVideoFrameInputSO.inputTexture
  -> encodeUnityTextureFrame(...)
  -> access units + MP4
```

工作项：

1. 新增或改造 runner，使其消费 `CurrentVideoFrameInputSO`，而不是自己创建 dynamic RT。
2. 先在 `SingleEncodePcSmoke.unity` 或独立测试场景验证 raw PCA。
3. 使用 `sourceCameraFrameId` / `timestampUnixMs` 作为 access unit metadata。

验收：

```text
CurrentVideoFrameInputSO.isValid = true
sourceKind = RawCameraImage
access units non-empty
MP4 extracted frame is real PCA
```

### Phase 3: Composite input

目标：

```text
PassthroughCameraLayerCompositor.compositeRenderTexture
  -> CurrentVideoFrameInputSO
  -> MediaCodec
```

工作项：

1. 在 `SampleScene` 或专用测试场景中设置 `compositedLayers`。
2. 放置明确可见的 overlay test object。
3. 确认 `CurrentVideoFrameInputSO.sourceKind = PassthroughUnityComposite`。

验收：

```text
MP4 extracted frame shows PCA + Unity overlay
access units still come from same MediaCodec path
```

### Phase 4: Access unit bus 正式化

目标：

```text
EncodedAccessUnit
  -> EncodedOutputMetadataBinder
  -> CaptureOutputQueueSO FramePacket
  -> MediaMuxer sample
```

工作项：

1. 定义正式 `EncodedAccessUnit` 数据结构。
2. 定义正式 encoder backend interface。
3. 把 Java returned bytes、MediaMuxer sample、`CaptureOutputRecord(FramePacket)` 统一到同一批 access units。
4. 绑定 `MergedFrameSnapshot` 或 sidecar metadata。

验收：

```text
FramePacket records carry codec, dimensions, keyframe/config flags, source frame id, timestamp.
FileArtifact records carry finalized MP4 path, manifest path, metadata sidecar path.
```

### Phase 5: SampleScene/DataCapture_Runtime 正式接线更新

目标：把老的 `DataCapture_Runtime` 从 Debug JPEG / InstantReplay / old UDP sender 过渡到 Single Encode bus。

当前已经先完成输出路由门控解耦；后续建议结构：

```text
DataCapture_Runtime/00_Handshake_RecordingControl
  OutputRouteGate
  LanDiscoveryClient
  RecordingSessionController

DataCapture_Runtime/50_EncodingNetwork
  10_SharedVideoFrameInput
  15_PassthroughUnityCompositor
  20_SingleEncodeAccessUnitBus
  25_EncodedOutput
  30_DebugLowFpsImage
  40_OutputSinks
      NetworkFramePacketSender
      LocalArtifactStore
      NetworkFileArtifactSender
```

这一步只在 Phase 1-4 通过后做，避免场景接线掩盖 bridge 问题。

### Phase 6: Sinks

目标：

```text
CaptureOutputQueueSO
  -> NetworkFramePacketSender
  -> LocalArtifactStore
  -> NetworkFileArtifactSender
```

工作项：

1. 复用 `CaptureOutputConsumerStateSO` 作为 consumer cursor。
2. `NetworkFramePacketSender` 只消费 `FramePacket + Stream`。
3. `LocalArtifactStore` 和 `NetworkFileArtifactSender` 只消费 `FileArtifact + OneShot`。
4. 本地和网络目标都作为 sink 配置，而不是编码器配置。

## 下一步建议

输出路由 gate 与握手解耦已经完成。下一轮代码推进回到 Phase 1：

```text
Unity dynamic RT
  -> native bridge
  -> MediaCodec input Surface
  -> access units
```

在 bridge blocker 解决前，不继续扩大 `SampleScene` 的编码和 sink 改造范围。当前 `OutputRouteGate` 接线保留，后续编码实现从该门控之后接入。
