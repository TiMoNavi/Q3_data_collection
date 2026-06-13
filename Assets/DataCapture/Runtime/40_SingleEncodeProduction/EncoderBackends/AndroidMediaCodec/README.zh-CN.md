# Single Encode Access Unit Bus

Last updated: 2026-06-12

## 当前结论

这套目录的目标不是“把 PCA 录成一个 MP4”。MP4 只是一个 sink。

真正目标是建立最终的视频输出架构：

```text
PCA / Unity composite RenderTexture
  -> one Unity render path
  -> one Android MediaCodec H264/H265 encode
  -> one access unit bus
       -> realtime network stream
       -> MediaMuxer MP4
       -> CaptureOutputQueueSO
       -> local artifact
       -> later file transfer
```

中心抽象应当是：

```text
Single Encode Access Unit Bus
```

也就是说，实时网络流、MP4、本地文件、文件发送都消费同一批 MediaCodec output samples / access units。它们不应该各自重新编码。

## 架构硬原则

这些原则优先于任何具体 smoke test 或临时实现。

### 1. 输入来源必须可替换

编码器不应该直接绑定 PCA、compositor 或某个场景对象。编码器只消费统一视频输入边界：

```text
CurrentVideoFrameInputSO
  inputTexture
  sourceKind
  sourceCameraFrameId
  timestampUnixMs
  sourceResolution
  outputResolution
  frameRate
  bitrateKbps
  codec
```

输入可以来自：

```text
RawCameraImage
  PassthroughCameraAccess.GetTexture()

PassthroughUnityComposite
  PCA texture + Unity overlay layer
  -> composite RenderTexture

Future source
  another texture / stream
```

只要最终能解析成同一份 `CurrentVideoFrameInputSO`，下游编码和输出链路就不应感知输入来源变化。

### 2. 编码产物必须携带上一阶段元数据

access unit 不能只是裸 bytes。编码产物至少要绑定：

```text
sourceCameraFrameId
timestampUnixMs
presentationTimeUs
codec
width / height
isKeyFrame
isCodecConfig
metadata binding status
```

它还应能关联上一阶段合成 / 同步层产出的 `MergedFrameSnapshot`，或在文件输出时写入 sidecar metadata。这样网络实时流、MP4、回放和离线重建才能重新对齐画面、pose、controller、camera intrinsics 等上下文。

### 3. 编码形式必须可替换

当前 Quest 主线是 Android `MediaCodec` H264/H265，但架构不能写死某一种编码器。正式接口应预留 backend 更换窗口：

```text
ITextureVideoEncoder
  AndroidMediaCodecH264
  AndroidMediaCodecH265
  future WebRTC / software / file encoder
```

上游只提供统一 texture input，下游只消费统一 access unit / output record。编码 backend 更换时，不应重写 PCA、composite、metadata 或 network 层。

### 4. 输出必须有统一接口

网络层不应该理解 PCA、Unity composite、MediaCodec、MediaMuxer 或 InstantReplay 细节。它只消费统一输出层：

```text
CaptureOutputQueueSO
  CaptureOutputRecord(FramePacket)
  CaptureOutputRecord(FileArtifact)
```

每个 sink 自己维护 consumer cursor，例如：

```text
NetworkFramePacketSender
NetworkFileArtifactSender
LocalArtifactStore
```

### 5. 视频流和 MP4 必须同时服务本地与网络目标

最终输出不是“本地或云端二选一”，也不是“实时流或文件二选一”。目标形态应是：

```text
same H264/H265 access units
  -> realtime video stream
  -> MediaMuxer MP4 samples
  -> CaptureOutputQueueSO FramePacket

finalized MP4
  -> CaptureOutputQueueSO FileArtifact
  -> local storage
  -> cloud / PC / network receiver
```

因此实现顺序必须服务这个中心：先打通真实 Unity/PCA/composite texture 到 MediaCodec access units，再接 MP4、本地和网络 sink。

### 6. 输出路由门控必须位于握手之前

录制是否需要 PC / 云端握手，不属于握手实现自身的职责。必须先根据输出目标经过独立门控：

```text
NetworkSenderConfigurationSO.outputTarget
  -> OutputRouteGateController
  -> OutputRouteGateSO
       LocalFile
         -> skip network handshake
         -> allow recording

       RemoteReceiver / SelfReceiver / mixed network target
         -> enable handshake stage
         -> wait for PCReceiverConnectionStatusSO
         -> allow recording after handshake
```

责任边界：

- `OutputRouteGateController` 判断当前输出模式是否需要网络握手，并控制握手阶段是否启用。
- `OutputRouteGateSO` 是录制层和传输层读取的统一门控结果。
- `LanDiscoveryClient` 只负责 discovery / handshake，不再读取 `UsesNetwork`，也不再解释本地模式。
- `RecordingSessionController` 只读取 `OutputRouteGateSO.CanStartRecording`，不直接依赖网络配置或 PC receiver 状态。
- `CaptureTransmissionGateReporter` 的第一项是 `outputRouteReady`，不是固定的 `pcReceiverReady`。

这条规则必须保持：本地模式的握手跳过逻辑不得重新散落进录制、编码、发送或握手组件。

## 当前已经证明

### PCA 输入

Meta 当前 PCA 文档已重新核对。Unity 侧 live camera texture 仍以 `PassthroughCameraAccess` 为事实来源。

本项目里已经实机证明：

```text
PassthroughCameraAccess.GetTexture()
  -> Graphics.Blit
  -> staging RenderTexture
  -> InstantReplay UnboundedRecordingSession
  -> real camera MP4
```

证据：

```text
captures/device_debug_20260611_1506_pca_direct/pca_direct_20260611_070602.mp4
captures/device_debug_20260611_1506_pca_direct/frames/frame10.png
```

`ffprobe` 结果是 H264、1280x960、约 5.09 秒、154 帧。抽帧确认是真实桌面、Quest 控制器、杯子等 PCA 画面。

这条链路只证明真实 PCA 像素可以进入视频文件。它不是最终架构。

### 同一批编码产物可服务多种输出

PC smoke 已证明数据模型成立：

```text
one encoded H264 stream
  -> split into FramePacket records
  -> remux into MP4 FileArtifact
  -> publish both to CaptureOutputQueueSO
```

Android Java A0 也已证明：

```text
Q3SurfaceVideoEncoder.startWithMp4(...)
  -> MediaCodec output sample
       -> return byte[] access unit to C#
       -> write the same sample to MediaMuxer
```

这证明 bus 方向是可行的。

### 纯蓝 / 纯色 MP4 的定位

历史上的 `SingleEncodeAndroidMuxerSmoke_20260610_193429_capture.mp4` 不是 PCA 链路失败。

它来自 Java synthetic pattern 路径：

```text
SingleEncodeAndroidMuxerSmokeRunner
  -> Q3SurfaceVideoEncoder.encodePatternFrame(...)
  -> EglPatternRenderer.drawFrame(...)
```

该路径只画测试色块进 MediaCodec input Surface。它可以观察 `CurrentVideoFrameInputSO` 的 frame id / timestamp，但没有消费 Unity/PCA texture。

所以纯色 MP4 的含义是：

```text
MediaCodec + MediaMuxer works.
Unity/PCA texture is not yet the encoder input.
```

不要再把这个结果解释成 PCA、Camera access 或 muxer 故障。

## 当前真正未完成

核心缺口只有一个：

```text
Unity RenderTexture / VkImage
  -> Vulkan native bridge
  -> MediaCodec input Surface backed image
  -> MediaCodec drain access units
```

2026-06-11 的实机 dynamic bridge smoke 已证明两个端点都可见：

```text
Unity RenderTexture -> IUnityGraphicsVulkan.AccessTexture: passed
MediaCodec input Surface -> ANativeWindow_fromSurface: passed
```

证据：

```text
captures/device_debug_20260611_1722_unity_dynamic_bridge/manifest.json
```

关键字段：

```text
graphicsDeviceType = Vulkan
nativeBridgeVulkanReady = true
lastAccessTextureOk = true
encoderSurfaceAttached = true
encoderWindowWidth/Height = 320x320
bridgeAttemptCount = 60
bridgeOutputFrameCount = 0
patternFallbackFrameCount = 0
muxedSampleCount = 0
```

这不是回退。它把未知范围缩小到了“如何把 Unity VkImage 写进 encoder Surface 对应图像”。

当前 Java `encodeUnityTextureFrame(...)` 仍是占位实现，会返回空 access unit。下一步不能再优化文档或绕到 InstantReplay，而是要实现这一步 GPU bridge。

## 当前代码地图

### 输入侧

```text
PassthroughCameraFrameWriter
  -> CurrentCameraImageSO
  -> CurrentCameraStreamStateSO
  -> CurrentCameraPoseSO
  -> CurrentCameraMetadataSO

VideoFrameInputResolver
  -> CurrentVideoFrameInputSO
```

`CurrentVideoFrameInputSO` 是正式视频输入边界。编码器不应该自己找 PCA 或 compositor。

输入可来自：

```text
RawCameraImage
  PassthroughCameraAccess.GetTexture()
  -> shared staging RenderTexture

PassthroughUnityComposite
  PassthroughCameraLayerCompositor.compositeRenderTexture
  -> shared staging RenderTexture
```

当前 active scene 和 Android build scene 已切回：

```text
Assets/Scenes/SampleScene.unity
```

正式接线位于 `SampleScene/DataCapture_Runtime`。其中：

```text
00_Handshake_RecordingControl
  OutputRouteGate
    OutputRouteGateController
      -> NetworkSenderConfiguration.asset
      -> PCReceiverConnectionStatus.asset
      -> PCDiscoveryRequest.asset
      -> OutputRouteGate.asset
      -> controls LanDiscoveryClient.enabled

50_EncodingNetwork
  10_SharedVideoFrameInput
  15_PassthroughUnityCompositor
  20_LocalMp4Save
  25_EncodedOutput
  30_DebugLowFpsImage (inactive)
```

当前 `NetworkSenderConfiguration.asset` 是 `LocalFile`，所以：

```text
OutputRouteGateController
  -> requiresNetworkHandshake = false
  -> LanDiscoveryClient disabled
  -> OutputRouteGateSO.canStartRecording = true
```

切换到网络输出目标时，前置 gate 会启用 `LanDiscoveryClient`，并在握手成功前阻止录制开始。

### 编码侧

```text
Q3SurfaceVideoEncoder.startWithMp4(...)
  -> MediaCodec.createEncoderByType(video/avc or video/hevc)
  -> MediaCodec.createInputSurface()
  -> optional MediaMuxer
  -> nativeAttachEncoderSurface(inputSurface, width, height)
```

已可用：

```text
encodePatternFrame(...)
  synthetic EGL color pattern
  -> MediaCodec
  -> byte[] access unit
  -> MediaMuxer sample
```

未完成：

```text
encodeUnityTextureFrame(...)
  Unity native texture pointer
  -> currently logs status and returns empty byte[]
```

### Native bridge

```text
q3dc_vulkan_bridge
  -> UnityPluginLoad
  -> IUnityGraphicsVulkan
  -> Q3DC_SetUnityTexture
  -> GL.IssuePluginEvent
  -> AccessTexture
  -> nativeAttachEncoderSurface
  -> ANativeWindow_fromSurface
```

当前 native bridge 是端点证明，不是像素拷贝实现。

### 输出侧

```text
CaptureOutputRecord
CaptureOutputQueueSO
CurrentCaptureOutputSO
EncodedOutputMetadataBinder
CaptureOutputConsumerStateSO
```

这些类型是 bus 下游的公共 envelope / queue / cursor 层。

目前已有 state asset：

```text
NetworkFramePacketConsumerState.asset
NetworkFileArtifactConsumerState.asset
LocalFileArtifactConsumerState.asset
```

真正的 consumer 组件仍未完成：

```text
NetworkFramePacketSender
NetworkFileArtifactSender
LocalArtifactStore
```

## 历史文档说明

本目录下 2026-06-10 的文档是历史验证记录，不再是当前实施入口：

```text
validation-notes-2026-06-10.zh-CN.md
current-findings-next-steps-2026-06-10.zh-CN.md
```

2026-06-11 的文档保留实机证据和缩小后的问题边界：

```text
final-architecture-status-validation-2026-06-11.zh-CN.md
```

下一阶段推进顺序和 `SampleScene/DataCapture_Runtime` 接线审计见：

```text
implementation-plan-scene-wiring-2026-06-12.zh-CN.md
```

新的实施入口以本 README 为准。

## 下一步

优先级顺序：

1. 在 native bridge 中实现 Unity `VkImage` 到 encoder input Surface 图像的 Vulkan copy / blit / render pass。
2. 让 `encodeUnityTextureFrame(...)` 真正提交一帧并 drain access units。
3. 先用 `UnityDynamicTextureEncoderSmokeRunner` 的动态测试 RT 验证非纯色 MP4 和非空 access units。
4. 再把输入切到 `CurrentVideoFrameInputSO.inputTexture`，验证 raw PCA。
5. 接上 `PassthroughCameraLayerCompositor`，验证 PCA + Unity overlay composite。
6. 最后实现 `NetworkFramePacketSender`、`NetworkFileArtifactSender`、`LocalArtifactStore`。

在第 1-3 步完成前，不要把 InstantReplay 原型当成最终方案，也不要把 Java pattern MP4 当成真实摄像头编码结果。
