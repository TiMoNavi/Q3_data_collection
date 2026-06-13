# 10 SO 调试层设计：前向 IF 诊断链路

Last updated: 2026-06-10

## 硬约束：代码、场景组件、SO 实例必须一起落地

只改 C# 代码没有用。调试链路要在 Play Mode 和 Quest 真机运行，必须同时完成三件事：

1. 写运行时代码。
2. 用 Unity MCP / Unity skill 进入场景，把 `MonoBehaviour` 组件挂到正确 GameObject。
3. 在 `Assets/SOData` 下创建或复用 ScriptableObject 实例，并把实例引用绑定到场景组件。

如果新增或改动了 `DataCaptureSODebugPipeline.cs` 及各层 `*DebugLayer.cs`，但没有进 Unity 场景挂组件、没有绑定 SOData 实例、没有保存场景，那么这段代码不会参与运行时调试链路。

### 当前场景拼装位置

当前 `SampleScene` 中的数据采集运行时根节点是：

```text
DataCapture_Runtime
```

现有父子结构：

```text
DataCapture_Runtime
  00_Handshake_RecordingControl
  10_CurrentSOInputs
  20_QueueBuffers
  30_Synchronization
  40_EncodingDecode
  50_NetworkSend
  80_StatusPreview
  90_AI_AutoDebug
    SO_WriteRequestBridge
      SOFieldWriteRequestConsumer
      SOFieldWriteRequestFileBridge
      SORegistryListResponder
      SOValueAccessController
    Tests
      SO_Debug_Probe
        DataCaptureSODebugPipeline
        ControllerButtonDiscoveryRequestListener
      SO_Driven_MergeLayer_Test (inactive legacy)
        DataCaptureSoDrivenAutoRecordingTest
      SO_Driven_EncodingSwitch_Test (inactive legacy)
        DataCaptureSoDrivenEncodingSwitchTest
      MediaCodec_Surface_Smoke_Test
        SurfaceVideoEncoderSmokeRunner
```

新的调试器建议挂在：

```text
DataCapture_Runtime/90_AI_AutoDebug/Tests/SO_Debug_Probe
```

组件：

```text
DataCaptureSODebugPipeline
ControllerButtonDiscoveryRequestListener
```

如果需要保留通用 ADB 写入桥，继续使用：

```text
DataCapture_Runtime/90_AI_AutoDebug/SO_WriteRequestBridge
```

但 ADB/FileBridge 只负责写入请求，不负责状态判断。

### SO 实例创建位置

SO 类型定义在 `Assets/SObasic` 下只是 C# 类型，不等于有可运行的 SO 实例。

所有运行时引用的 ScriptableObject 实例必须在 Unity 里创建到：

```text
Assets/SOData
```

当前 SOData 层级：

```text
Assets/SOData/DataCapture
  00_Global
  10_CameraCapture
  20_VirtualLayerCapture
  30_PoseMetadataCapture
    Controller
    Headset
    NetworkDevice
  40_MergedSynchronization
  50_EncodingNetwork
  90_Diagnostics
  CoordinateSystemCalibration
```

调试相关可选 SO 实例，例如 `SODebugProbeSummary.asset`，应该创建在：

```text
Assets/SOData/DataCapture/90_Diagnostics
```

创建和绑定规则：

- 新增 SO 类型后，必须用 Unity AssetDatabase / Unity MCP 创建 `.asset` 实例。
- 场景组件必须引用 `Assets/SOData` 下的实例，不引用 `Assets/SObasic` 下的类型定义。
- 需要被通用读写访问的 SO，必须注册进 `SOStateViewer` 或 `SOFieldWriteRequestConsumer.writableAssets`。
- 组件挂好、引用绑定好后必须保存 scene 和 asset。

后续实现时应使用 Unity MCP 工具完成这些动作，而不是只改文件：

```text
find_gameobjects
manage_gameobject
manage_components
manage_scriptable_object
manage_scene save
```

当前通用 SO 访问组件：

```text
Assets/DataCapture/Testing/Runtime/SOValueAccessController.cs
Assets/DataCapture/Testing/Runtime/SORegistryListResponder.cs
Assets/SObasic/Runtime/SOAccess/SOValueAccessUtility.cs
Assets/SObasic/Runtime/ScriptableObjects/DataCapture/90_Diagnostics/SORegistryListRequestSO.cs
```

当前场景绑定：

```text
DataCapture_Runtime/90_AI_AutoDebug/SO_WriteRequestBridge
  SOFieldWriteRequestConsumer
  SOFieldWriteRequestFileBridge
  SORegistryListResponder
  SOValueAccessController
```

当前 SO 实例：

```text
Assets/SOData/DataCapture/90_Diagnostics/SORegistryListRequest.asset
```

## 目标

调试层只做三件事：

1. 通过通用 SO 写入入口模拟用户操作或外部触发。
2. 逐层读取正式链路已有 SO 字段，判断当前层是否正常。
3. 用 `UnityEngine.Debug` 输出当前层状态、失败原因和停止位置。

调试层不应该为每一个测试 case 新增一套 request/state/log SO。正式链路的状态已经保存在 SO 字段里，调试代码应该直接读这些字段，而不是复制它们。

运行时代码放在：

```text
Assets/DataCapture/Testing/Runtime
```

通用 SO 类型和必要 SO 操作放在：

```text
Assets/SObasic
```

正式 SO 实例读取自：

```text
Assets/SOData
```

## 基本模型

诊断是前向链，不是并行检测。

```text
网络握手
  -> 录制事件
  -> Current SO 捕捉
  -> Queue 写入
  -> TimestampMerger 同步
  -> Transmission Gate
  -> 编码输出
  -> 网络 packet
  -> PC 接收证据
```

每一层都遵守同一规则：

```text
if 当前层条件满足:
    Debug.Log 当前层通过，并触发或等待下一层
else:
    Debug.LogWarning 或 Debug.LogError 当前层失败原因
    return，后续层不再判断
```

这样失败会停在最早的真实 blocker 上。前一层失败时，后一层没有诊断意义。

### 当前临时编码范围

当前编码层正在重做，自动调试链暂时只启用低频 Debug 图片线路：

```text
EncodingPipelineConfiguration.pipelineMode = DebugImageOnly
EncodingPipelineConfiguration.videoEncoderBackend = DebugJpeg
DebugImageStreamSettings.maxFramesPerSecond = 2
DebugImageStreamSettings.maxDimension = 320
DebugImageStreamSettings.jpegQuality = 70
DebugImageStreamSettings.requireSendableMergedSnapshot = true
```

当前不启用 `VideoOnly`、`DebugImageAndVideo`、`AndroidMediaCodecH264`、`AndroidMediaCodecH265` 或 `WebRtc` 调试分支。下面表格里的视频/H264/H265 行保留为后续设计对照，不是当前运行路径。

## 分层诊断表

| 层级 | 判断当前层正常需要读取的 SO 条件 | 推进下一层需要通过哪些 SO 模拟触发 | 异常判断条件 | Debug 输出重点 |
|---|---|---|---|---|
| 0 网络握手 | `PCReceiverConnectionStatus.handshakeSucceeded == true`; `CanStartRecording == true`; `remoteHost` 非空; `metadataPort/videoPort > 0`; `NetworkSenderConfiguration.remoteHost` 已写入 | 写 `PCDiscoveryRequest.requested = true` 或调用 `PCDiscoveryRequestSO.Request("SODebugProbe")` | `handshakeSucceeded == false`; `phase` 卡在 `WaitingForResponse/SocketError/MalformedResponse/IncompatibleResponse`; `lastBlocker` 非空 | `phase`, `lastBlocker`, `lastErrorMessage`, `lastDiscoveryTargets`, `networkWarning` |
| 1 录制事件 | `RecordingSessionState.State == WarmingUp or Recording`; `ShouldWriteQueues == true`; `HasException == false` | 写 `RecordingToggleRequest.requested = true` 或调用 `RecordingToggleRequestSO.Request("SODebugProbe")`; 可选用 `CurrentControllerPose.leftSecondaryButtonPressed` 模拟按钮 | `ShouldWriteQueues == false`; `State == NotStarted`; `HasException == true` | `State`, `ShouldWriteQueues`, `LastExceptionReason`, `LastDebugMessage` |
| 2 Current SO 捕捉 | `CurrentCameraImage.isValid == true`; `currentTexture != null`; `frameId > baseline`; `CurrentCameraFrameTiming.isValid`; `CurrentCameraPose.isValid`; `CurrentCameraMetadata.isValid`; `CurrentCameraStreamState.isValid`; `CurrentCameraStreamState.isPlaying == true`; `CurrentControllerPose.isValid == true` | 不额外触发，正式 capture writer 应持续写 Current SO；调试层只等待新 frame | 任一 required Current SO 无效; `CurrentCameraImage.currentTexture == null`; `frameId` 不增长; stream 未 playing 或未 updated | 每个 Current SO 的 `isValid`, `frameId`, `timestampUnixMs`, `resolution`, `isPlaying`, `isUpdatedThisFrame` |
| 3 Queue 写入 | `CameraImageQueue.Count > baseline`; `CameraFrameTimingQueue.Count > baseline`; `CameraPoseQueue.Count > baseline`; `CameraMetadataQueue.Count > baseline`; `CameraStreamStateQueue.Count > baseline`; `ControllerPoseQueue.Count > baseline`; required queue 的 `NewestTimestamp` 增长 | 上一层录制已经让 `ShouldWriteQueues == true`，不再手动触发 queue | `ShouldWriteQueues == true` 但 required queue count 不增长; required queue 长时间为空; `NewestTimestamp` 不刷新 | 每个 queue 的 `Count`, `Capacity`, `NewestTimestamp`, `OverwriteCount`, `GenerationId` |
| 4 TimestampMerger 同步 | `TimestampMergerDebugState.mergedCount > baseline`; `latestCameraFrameId > 0`; `latestStatus == Complete`; `latestIsSendable == true`; `MergedFrameSnapshotQueue.Count > baseline` | 不直接调用 merger；只等待 queue 输入驱动 merger 输出 | `latestIsSendable == false`; `latestMissingRequiredStreamMask` 包含 required stream; `latestDropReason` 非空; `mergedCount` 不增长 | `latestStatus`, `latestIsSendable`, `latestMissingRequiredStreamMask`, `latestMatchedStreamMask`, `latestDropReason`, `mergedCount` |
| 5 Transmission Gate | `CaptureTransmissionGate.pcReceiverReady == true`; `recordingActive == true`; `synthesisHealthy == true`; `canEncodeAndSend == true` | 不直接触发 sender；gate 由握手、录制、merger 状态自动更新 | `canEncodeAndSend == false`; 三个 gate 中任一为 false | `pcReceiverBlocker`, `recordingBlocker`, `synthesisBlocker`, `activeBlocker` |
| 6 编码输出 | `EncodingPipelineConfiguration.pipelineMode` 符合测试目标; `videoEncoderBackend` 符合测试目标; `CurrentEncodedFrame.isValid == true`; `codec == expectedCodec`; `sourceCameraFrameId > baseline`; `byteLength > 0`; 或 `EncodedFrameQueue` 中存在匹配记录 | 写 `EncodingPipelineConfiguration.pipelineMode`; 写 `EncodingPipelineConfiguration.videoEncoderBackend`; 可选写 `allowDebugImageDuringVideo` | 无匹配 codec; `byteLength <= 0`; `sourceCameraFrameId` 没超过 baseline; H264/H265 case 输出成 `DEBUG_MJPEG` | `pipelineMode`, `videoEncoderBackend`, `codec`, `sourceCameraFrameId`, `encodedFrameId`, `byteLength`, encoder 组件 `lastStatus` |
| 7 网络 packet | `CurrentNetworkPacket.isValid == true`; `NetworkPacketQueue.Count > baseline`; 最新 packet 的 `frameId/source frame` 与编码输出对应; packet stream 包含 metadata 和 video/debug | 不直接调用 network sender；由 gate 和 encoded frame 驱动 | 编码已成功但 packet queue 不增长; packet byte length 超过 `NetworkSenderConfiguration.maxPacketBytes`; expected codec 没有对应 packet | `streamName`, `frameId`, `sequenceId`, `byteLength`, `maxPacketBytes`, packet queue count |
| 8 PC 接收证据 | 外部 receiver 文件或日志出现对应 frame/codec; 可由外部脚本回写一个通用结果字段 | 由外部 PC receiver 或脚本回写结果；Unity 内不假装 PC 已接收 | Unity 已发送 packet，但 PC 没有对应 metadata/image/video 文件 | expected codec, expected frame id, receiver 目录, 外部脚本回写结果 |

## 状态推进表

诊断表解决“怎么看”。状态推进表解决“怎么主动推进到下一层”。

### 正式触发逻辑对照表

调试层必须对照正式链路原本的触发方式，不能自己创造一条新链路。

| 链路边界 | 正式链路原本的触发逻辑 | 正式组件 | 关键 SO | 调试层对照做法 | 下一阶段成立条件 |
|---|---|---|---|---|---|
| 未握手 -> 网络握手 | `LanDiscoveryClient` 监听 `PCDiscoveryRequestSO.requested`，或启动时 `discoverOnStart` 自动发起 discovery | `LanDiscoveryClient` | `PCDiscoveryRequest`; `PCReceiverConnectionStatus`; `NetworkSenderConfiguration` | 写 `PCDiscoveryRequest.requested = true`，或调用 `PCDiscoveryRequestSO.Request("SODebugProbe")` | `PCReceiverConnectionStatus.phase == Paired`; `handshakeSucceeded == true`; `CanStartRecording == true`; `NetworkSenderConfiguration.remoteHost` 和端口有效 |
| 网络握手 -> 允许录制 | 录制控制器的第一道门控检查 `PCReceiverConnectionStatusSO.CanStartRecording` | `RecordingSessionController` | `PCReceiverConnectionStatus`; `RecordingSessionState` | 不写 `RecordingSessionState`，只等待握手 SO 达标；失败就停在网络层 | `PCReceiverConnectionStatus.CanStartRecording == true` |
| 用户输入 -> 录制请求 | 控制器按钮或 UI 触发 `RecordingToggleRequestSO.Request()`；`RecordingToggleRequestConsumer` 监听 request 后调用 `RecordingSessionController.ToggleRecording()` | `ControllerButtonRecordingToggleListener`; `RecordingToggleRequestConsumer`; `RecordingSessionController` | `RecordingToggleRequest`; `RecordingSessionState`; `PCReceiverConnectionStatus` | 模拟点击开始录制时，写 `RecordingToggleRequest.requested = true` 或调用 `RecordingToggleRequestSO.Request("SODebugProbe")` | `RecordingSessionState.State == WarmingUp`，随后 `ShouldWriteQueues == true` |
| 录制请求 -> Queue 写入 | `RecordingSessionController.StartRecording()` 通过门控后设置 `RecordingSessionState.BeginWarmup()`；`CurrentToQueueRecorder` 在 `ShouldWriteQueues == true` 时从 Current 写入 Queue | `RecordingSessionController`; `CurrentToQueueRecorder` | `RecordingSessionState`; `Current*`; `*Queue` | 不直接写 queue；只用录制 request 进入 `ShouldWriteQueues`，再观察 queue count 是否增长 | required queue 的 `Count` 和 `NewestTimestamp` 增长 |
| Current SO -> Queue | capture writer 持续写 `Current*`；`CurrentToQueueRecorder.RecordCurrent()` 检查 `source.IsRecordValid`、类型兼容和新 timestamp/sequence 后写 queue | `PassthroughCameraFrameWriter`; `ControllerPoseCapture`; `CurrentToQueueRecorder` | `CurrentCameraImage`; `CurrentCameraFrameTiming`; `CurrentCameraPose`; `CurrentCameraMetadata`; `CurrentCameraStreamState`; `CurrentControllerPose`; matching queues | 不改 Current/Queue result；只检查 Current 是否 valid、frameId 是否增长，queue 是否随录制增长 | 所有 required Current valid；所有 required Queue 有新记录 |
| Queue -> 合成层 | `TimestampMerger` 以 `CameraFrameTimingQueue` 为锚点，按 tolerance 匹配 required streams；时序对得上才产生 sendable snapshot | `TimestampMerger` | `CameraFrameTimingQueue`; required queues; `CompositeAlignmentConfiguration`; `TimestampMergerDebugState`; `MergedFrameSnapshotQueue` | 不直接调用 merger，不写 merger result；只等待时序匹配 | `TimestampMergerDebugState.latestStatus == Complete`; `latestIsSendable == true`; `latestMissingRequiredStreamMask == None/0` |
| 合成层 warmup -> Recording | `TimestampMerger` 在 required streams 首次完整匹配后调用 `RecordingSessionState.StartRecording()`，从 `WarmingUp` 进入 `Recording` | `TimestampMerger`; `RecordingSessionStateSO` | `TimestampMergerDebugState`; `RecordingSessionState`; `MergedFrameSnapshotQueue` | 不手写 `RecordingSessionState.State`；等待 merger 完成 warmup | `RecordingSessionState.IsRecording == true`; `TimestampMergerDebugState.latestIsSendable == true` |
| 三个门控 -> 发送 Gate | `CaptureTransmissionGateReporter` 每帧汇总三道门控：PC ready、recording active、synthesis healthy | `CaptureTransmissionGateReporter` | `PCReceiverConnectionStatus`; `RecordingSessionState`; `TimestampMergerDebugState`; `QueueDebugState`; `CaptureTransmissionGate` | 不写 `CaptureTransmissionGate`；只读取三道门控是哪一道没开 | `CaptureTransmissionGate.pcReceiverReady == true`; `recordingActive == true`; `synthesisHealthy == true`; `canEncodeAndSend == true` |
| Gate + 编码配置 -> 编码输出 | Debug JPEG streamer 或 video encoder 在 update 中检查 `EncodingPipelineConfiguration`、`CaptureTransmissionGate.Active`、`RecordingSessionState.IsRecording`、Current image 和 sendable merged snapshot | `AsyncDebugJpegNetworkStreamer`; `AsyncMjpegVideoStreamEncoder`; `VideoStreamEncoderRunner` | `EncodingPipelineConfiguration`; `CurrentCameraImage`; `CurrentCameraStreamState`; `CaptureTransmissionGate`; `RecordingSessionState`; `MergedFrameSnapshotQueue`; `CurrentEncodedFrame`; `EncodedFrameQueue` | 写 `EncodingPipelineConfiguration.pipelineMode` 和 `videoEncoderBackend` 选择 Debug JPEG/MJPEG/H264/H265；等待编码 SO 输出 | `CurrentEncodedFrame.isValid == true`; `codec == expectedCodec`; `byteLength > 0`; `EncodedFrameQueue.Count` 增长 |
| 编码输出 -> 网络发送 | encoder 产出 `EncodedFrameRecord` 后调用 `VideoPacketSender.Send()`；metadata sender 也会记录 packet header | `VideoPacketSender`; `MetadataPacketSender` | `CurrentEncodedFrame`; `EncodedFrameQueue`; `CurrentNetworkPacket`; `NetworkPacketQueue`; `NetworkSenderConfiguration` | 不直接调用 sender；等待 encoded frame 和 gate 驱动 packet 输出 | `NetworkPacketQueue.Count` 增长；`CurrentNetworkPacket.isValid == true`; packet byteLength 不超过配置上限 |
| 网络发送 -> PC 接收 | Unity 侧只能证明 packet 已生成/发送；PC receiver 是否写文件属于外部证据 | `q3dc_receiver.py`; `q3dc_receiver_gui.py` | Unity 侧：`NetworkPacketQueue`; 外部：receiver 文件/日志 | Unity Debug 只输出 expected frame/codec/payload；外部脚本可回写一个通用结果字段 | receiver 目录出现对应 metadata/debug image/video 文件或日志记录 |

推进只允许改 request/config/input SO，不应该直接篡改 result/diagnostic SO 来伪造成功。例如：

- 可以写 `PCDiscoveryRequest.requested` 触发握手。
- 可以写 `RecordingToggleRequest.requested` 触发开始录制。
- 可以写 `EncodingPipelineConfiguration.pipelineMode` 改编码模式。
- 不应该直接写 `PCReceiverConnectionStatus.handshakeSucceeded = true` 伪造握手成功。
- 不应该直接写 `RecordingSessionState.State = Recording` 绕过正式录制控制器。
- 不应该直接写 `TimestampMergerDebugState.latestIsSendable = true` 伪造合并成功。

| 目标阶段 | 主动推进操作 | 推荐写入的 SO 字段 | 触发后应该等待的 SO 条件 | 失败时必须输出的 SO 字段和值 |
|---|---|---|---|---|
| 进入网络握手 | 发起 PC discovery | `PCDiscoveryRequest.requested = true`; 或 `PCDiscoveryRequestSO.Request("SODebugProbe")` | `PCReceiverConnectionStatus.phase == Paired`; `handshakeSucceeded == true`; `CanStartRecording == true` | `PCDiscoveryRequest.requested`; `PCDiscoveryRequest.requestRevision`; `PCReceiverConnectionStatus.phase`; `handshakeSucceeded`; `remoteHost`; `metadataPort`; `videoPort`; `lastBlocker`; `lastErrorMessage`; `networkWarning` |
| 进入录制 | 模拟用户点开始录制 | `RecordingToggleRequest.requested = true`; 或 `RecordingToggleRequestSO.Request("SODebugProbe")` | `RecordingSessionState.State == WarmingUp or Recording`; `ShouldWriteQueues == true` | `RecordingToggleRequest.requested`; `RecordingToggleRequest.requestRevision`; `RecordingSessionState.State`; `ShouldWriteQueues`; `IsRecording`; `HasException`; `LastExceptionReason`; `LastDebugMessage` |
| 等待 Current SO | 不主动写 result SO，等待正式 capture writer 写入 | 无；可选只记录 baseline：`CurrentCameraImage.frameId`; `CurrentCameraFrameTiming.frameId` | required Current SO 的 `isValid == true`; camera `frameId` 增长; `currentTexture != null` | `CurrentCameraImage.isValid`; `currentTexture`; `frameId`; `timestampUnixMs`; `resolution`; `CurrentCameraStreamState.isPlaying`; `isUpdatedThisFrame`; `CurrentControllerPose.isValid` |
| 等待 Queue | 不主动写 queue，录制态会驱动 `CurrentToQueueRecorder` | 无；前置条件是 `RecordingSessionState.ShouldWriteQueues == true` | required queue 的 `Count > baseline`; `NewestTimestamp` 增长 | `RecordingSessionState.ShouldWriteQueues`; 每个 required queue 的 `Count`; `NewestTimestamp`; `OldestTimestamp`; `OverwriteCount`; `GenerationId` |
| 等待 Merger | 不直接调用 merger，等待 queue 输入驱动同步 | 无；可选只记录 baseline：`TimestampMergerDebugState.mergedCount` | `TimestampMergerDebugState.latestIsSendable == true`; `mergedCount > baseline`; `MergedFrameSnapshotQueue.Count > baseline` | `mergedCount`; `latestCameraFrameId`; `latestStatus`; `latestIsSendable`; `latestRequiredStreamMask`; `latestMissingRequiredStreamMask`; `latestMatchedStreamMask`; `latestDropReason`; `statusMessage` |
| 打开发送 gate | 不主动写 gate，gate 应由握手、录制、merger 自动计算 | 无；前置条件是 handshake、recording、merger 已通过 | `CaptureTransmissionGate.canEncodeAndSend == true` | `pcReceiverReady`; `pcReceiverBlocker`; `recordingActive`; `recordingBlocker`; `synthesisHealthy`; `synthesisBlocker`; `canEncodeAndSend`; `activeBlocker` |
| 进入 Debug 图片编码 | 切换为图片输出 | `EncodingPipelineConfiguration.pipelineMode = DebugImageOnly`; `videoEncoderBackend = DebugJpeg` | `CurrentEncodedFrame.codec == DEBUG_JPEG`; `byteLength > 0`; `sourceCameraFrameId > baseline` | `EncodingPipelineConfiguration.pipelineMode`; `videoEncoderBackend`; `allowDebugImageDuringVideo`; `CurrentEncodedFrame.isValid`; `codec`; `sourceCameraFrameId`; `encodedFrameId`; `byteLength`; `debugFilePath` |
| 进入 MJPEG fallback 视频编码 | 切换为视频输出，但 backend 用 DebugJpeg | `EncodingPipelineConfiguration.pipelineMode = VideoOnly`; `videoEncoderBackend = DebugJpeg` | `CurrentEncodedFrame.codec == DEBUG_MJPEG`; `byteLength > 0`; 或 `EncodedFrameQueue` 存在匹配记录 | `pipelineMode`; `videoEncoderBackend`; `CurrentEncodedFrame.codec`; `byteLength`; `EncodedFrameQueue.Count`; 最新匹配 record 的 `codec/sourceCameraFrameId/byteLength` |
| 进入 H264 硬编 | 切换视频 backend 为 H264 | `EncodingPipelineConfiguration.pipelineMode = VideoOnly`; `videoEncoderBackend = AndroidMediaCodecH264` | `CurrentEncodedFrame.codec == H264`; `byteLength > 0`; `sourceCameraFrameId > baseline` | `pipelineMode`; `videoEncoderBackend`; `CurrentEncodedFrame.codec`; `isKeyFrame`; `byteLength`; `sourceCameraFrameId`; encoder 组件 `lastStatus`；如果 codec 是 `DEBUG_MJPEG` 必须明确报错 |
| 进入 H265 硬编 | 切换视频 backend 为 H265 | `EncodingPipelineConfiguration.pipelineMode = VideoOnly`; `videoEncoderBackend = AndroidMediaCodecH265` | `CurrentEncodedFrame.codec == H265`; `byteLength > 0`; `sourceCameraFrameId > baseline` | `pipelineMode`; `videoEncoderBackend`; `CurrentEncodedFrame.codec`; `isKeyFrame`; `byteLength`; `sourceCameraFrameId`; encoder 组件 `lastStatus`；如果 codec 是 `DEBUG_MJPEG` 必须明确报错 |
| 等待网络 packet | 不直接调用 sender，等待 encoded frame 和 gate 驱动发送 | 无；前置条件是 `canEncodeAndSend == true` 且编码输出通过 | `NetworkPacketQueue.Count > baseline`; `CurrentNetworkPacket.isValid == true`; packet 与 expected codec/frame 对应 | `CurrentNetworkPacket.isValid`; `streamName`; `frameId`; `sequenceId`; `byteLength`; `NetworkPacketQueue.Count`; `NetworkSenderConfiguration.maxPacketBytes`; expected codec/frame |
| 等待 PC 接收 | 外部 receiver 或脚本回写证据 | 可选写一个通用结果字段，例如 `SODebugProbeSummary.pcEvidence = ...`，不要为 PC 接收另建复杂状态机 | PC receiver 文件或日志出现 expected frame/codec | expected codec; expected frame id; receiver path; external result; Unity 侧最后 packet 信息 |

## Debug 输出字段要求

任何一个阶段失败时，Unity Debug 输出不能只写自然语言错误，必须携带对应 SO 字段和值。

最低格式：

```text
[SO-Debug][FAIL][LayerName]
target=<SO name>
condition=<field expected condition>
actual=<actual value>
fields=<field1=value1; field2=value2; field3=value3>
blocker=<blocker text>
timeout=<seconds>
```

示例：

```text
[SO-Debug][FAIL][Handshake] target=PCReceiverConnectionStatus condition=handshakeSucceeded==true actual=False fields=phase=WaitingForResponse; remoteHost=; metadataPort=0; videoPort=0; lastBlocker=Waiting for PC receiver discovery response. timeout=8
[SO-Debug][FAIL][Recording] target=RecordingSessionState condition=ShouldWriteQueues==true actual=False fields=State=NotStarted; IsRecording=False; HasException=False; LastExceptionReason= timeout=8
[SO-Debug][FAIL][Encoding] target=CurrentEncodedFrame condition=codec==H264 && byteLength>0 actual=codec=DEBUG_MJPEG fields=pipelineMode=VideoOnly; videoEncoderBackend=AndroidMediaCodecH264; sourceCameraFrameId=120; byteLength=34210 timeout=15
```

通过时也应该输出关键字段，方便确认推进成功：

```text
[SO-Debug][PASS][Handshake] fields=phase=Paired; host=192.168.1.10; metadataPort=5001; videoPort=5000
[SO-Debug][ACTION][Recording] write=RecordingToggleRequest.requested=True source=SODebugProbe
[SO-Debug][PASS][Merger] fields=mergedCount=42; latestCameraFrameId=120; latestIsSendable=True
```

## 推荐测试入口

调试入口不要再按测试 case 新建 SO。统一使用 `SOFieldWriteRequestSO` 做任意字段写入。

ADB 或外部脚本只需要写这种命令：

```json
{"source":"adb","targetName":"PCDiscoveryRequest","fieldPath":"requested","valueType":1,"boolValue":true}
{"source":"adb","targetName":"RecordingToggleRequest","fieldPath":"requested","valueType":1,"boolValue":true}
{"source":"adb","targetName":"EncodingPipelineConfiguration","fieldPath":"pipelineMode","valueType":7,"stringValue":"VideoOnly"}
{"source":"adb","targetName":"EncodingPipelineConfiguration","fieldPath":"videoEncoderBackend","valueType":7,"stringValue":"AndroidMediaCodecH264"}
```

更好的运行时做法是调试 MonoBehaviour 直接引用这些正式 SO，并通过正式 SO 的 `Request()` 方法触发：

```csharp
pcDiscoveryRequest.Request("SODebugProbe");
recordingToggleRequest.Request("SODebugProbe");
encodingPipelineConfiguration.pipelineMode = EncodingPipelineMode.VideoOnly;
encodingPipelineConfiguration.videoEncoderBackend = VideoEncoderBackend.AndroidMediaCodecH264;
```

## 通用 SO 读取与写入

调试链路必须使用通用类和通用方法访问 SO，不能为每一种 SO 写一个专用控制方法。

当前实现入口：

```text
SOValueAccessController
SOValueAccessUtility
SOFieldWriteRequestConsumer
SORegistryListResponder
```

### SO 获取边界

任意 SO 的定位只允许走统一 registry：

```text
SOStateViewer
  -> CopyAllScriptableObjects(results)
  -> targetName 匹配 asset.name / type.Name / type.FullName
```

或者走显式列表：

```text
SOFieldWriteRequestConsumer.writableAssets
```

规则：

- 场景里需要被调试、被写入、被观察的 SO，都必须注册进 `SOStateViewer` 或 `writableAssets`。
- 调试器不能自己去 `Resources.Load`、不能扫描硬编码路径、不能每个 SO 单独拖一个“控制器”。
- `targetName` 必须稳定，推荐使用 asset 名，例如 `PCReceiverConnectionStatus`、`RecordingToggleRequest`、`EncodingPipelineConfiguration`。

### 获取 SO 列表

调试器必须能在三种场景拿到当前可访问 SO 列表：

| 场景 | 使用方式 | 输出 |
|---|---|---|
| Unity Editor | 在 `SOValueAccessController` 右键 `Log Registered SO List`，或调用 `BuildRegisteredSOList()` | Unity Console |
| Runtime 函数调用 | `SOValueAccessController.CopyRegisteredAssets()` 或 `BuildRegisteredSOList()` | `List<ScriptableObject>` 或字符串 |
| ADB/Logcat | 通过 `SOFieldWriteRequestFileBridge` 写 `SORegistryListRequest.requested = true` | `SORegistryListRequest.lastListText` 和 Unity Debug `[SO-Access][LIST]` |

ADB 触发 SO 列表示例：

```json
{"source":"adb","targetName":"SORegistryListRequest","fieldPath":"requested","valueType":1,"boolValue":true}
```

成功时 Unity Debug 应输出：

```text
[SO-Access][LIST] SO registry contains N registered asset(s).
1. CurrentTimestamp type=... path=Assets/SOData/...
2. RecordingSessionState type=... path=Assets/SOData/...
```

同时 `SORegistryListRequest.asset` 会保存：

```text
lastSucceeded
lastListCount
lastStatusMessage
lastListText
recentEntries
```

### 通用读取方法

建议新增一个运行时通用 helper，例如：

```text
SOReflectionAccess.TryRead(target, fieldPath, out object value, out string error)
SOReflectionAccess.TryFormatValue(value, out string text)
```

`fieldPath` 使用点路径：

```text
handshakeSucceeded
phase
lastBlocker
latestMissingRequiredStreamMask
pipelineMode
videoEncoderBackend
```

读取逻辑：

```text
Split fieldPath by "."
从 target.GetType() 开始逐层查找 FieldInfo 或只读 PropertyInfo
读到最终值后转成字符串，用于 Debug 输出
失败时返回 error，不抛异常中断整条调试链
```

读取可以支持字段和只读属性，因为很多正式 SO 把关键状态暴露成属性：

```text
PCReceiverConnectionStatusSO.CanStartRecording
RecordingSessionStateSO.ShouldWriteQueues
RecordingSessionStateSO.IsRecording
EncodingPipelineConfigurationSO.AllowsVideo
```

调试输出示例：

```text
[SO-Debug][READ] PCReceiverConnectionStatus.phase=Paired
[SO-Debug][READ] RecordingSessionState.ShouldWriteQueues=True
[SO-Debug][READ] TimestampMergerDebugState.latestDropReason=
```

### 通用写入方法

写入也只走统一方法，例如：

```text
SOReflectionAccess.TryWrite(target, fieldPath, typedValue, out string error)
```

或者继续复用现有：

```text
SOFieldWriteRequestSO
SOFieldWriteRequestConsumer
SOFieldWriteRequestFileBridge
```

写入逻辑：

```text
Resolve target by targetName
Resolve fieldPath by reflection
Convert value by target field type
field.SetValue(target, value)
Debug.Log ACTION 或 FAIL
```

允许写入的类型应该先保持简单：

```text
string
bool
int
long
float
Vector2
Vector3
enum
```

不要为了某一个 SO 增加专用写入函数，例如：

```text
SetPcHandshake(...)
SetRecordingState(...)
SetEncodingMode(...)
SetMergerStatus(...)
```

这些都应该变成同一种操作：

```text
Write("PCDiscoveryRequest", "requested", true)
Write("RecordingToggleRequest", "requested", true)
Write("EncodingPipelineConfiguration", "pipelineMode", "VideoOnly")
Write("EncodingPipelineConfiguration", "videoEncoderBackend", "AndroidMediaCodecH264")
```

写入 Debug 示例：

```text
[SO-Debug][ACTION][WRITE] PCDiscoveryRequest.requested=True source=Inspector
[SO-Debug][ACTION][WRITE] RecordingToggleRequest.requested=True source=ADB
[SO-Debug][FAIL][WRITE] target=Foo field=bar error=No writable SO matched targetName
```

### Runtime 与 Editor 边界

运行时调试代码不能依赖 `UnityEditor.SerializedObject`。Quest 真机上只有 runtime reflection、正式 SO 引用和 Unity Debug。

推荐边界：

| 层 | 负责什么 | 不负责什么 |
|---|---|---|
| SO 架构 | 保存正式状态、配置、request、Current、Queue、Diagnostics | 不保存每个测试 case 的重复状态机 |
| 通用 SO 访问层 | 通过 targetName + fieldPath 读取/写入任意已注册 SO | 不知道业务含义，不判断链路是否正常 |
| 调试诊断层 | 按层级 `if/else` 读取 SO 条件并输出 Debug | 不维护独立业务状态，不替代正式 SO |
| ADB/FileBridge | 把外部命令送进 `SOFieldWriteRequestSO` | 不判断流程，不承担状态展示 |
| Unity Debug/Logcat | 输出状态、错误、异常、停止层级 | 不作为业务数据存储 |

## ADB 与 Unity Debug 的职责边界

ADB 调试不应该变成状态系统。它只负责两件事：

1. 把外部命令写进通用 SO 写入入口。
2. 通过 Logcat 抓 Unity Debug 输出。

整条调试链路的状态、错误、异常信息必须由 Unity Debug 输出，因为这样在 Quest 真机上可以直接用 ADB/Logcat 搜索：

```text
[SO-Debug][ACTION]
[SO-Debug][PASS]
[SO-Debug][WAIT]
[SO-Debug][FAIL]
```

错误必须包含：

```text
layer
target SO
field
actual value
expected condition
blocker
timeout seconds
```

示例：

```text
[SO-Debug][FAIL][Handshake] target=PCReceiverConnectionStatus field=handshakeSucceeded actual=False expected=True phase=WaitingForResponse blocker=Waiting for PC receiver discovery response.
[SO-Debug][FAIL][Recording] target=RecordingSessionState field=ShouldWriteQueues actual=False expected=True state=NotStarted exception=
[SO-Debug][FAIL][Merger] target=TimestampMergerDebugState field=latestIsSendable actual=False expected=True missing=CameraImage latestDropReason=Missing required stream.
```

## IF 链伪代码

当前集成调试器为：

```text
90_IntegratedChain/DataCaptureSODebugPipeline.cs
```

它是普通 `MonoBehaviour`，放在：

```text
Assets/DataCapture/Testing/Runtime/90_IntegratedChain
```

它只做中心编排。每个层级的 SO 条件判断和 SO 推进动作分别放在：

```text
00_Handshake_RecordingControl/HandshakeRecordingControlDebugLayer.cs
10_CurrentSOInputs/CurrentSOInputsDebugLayer.cs
20_QueueBuffers/QueueBuffersDebugLayer.cs
30_Synchronization/SynchronizationDebugLayer.cs
40_EncodingDecode/EncodingDecodeDebugLayer.cs
50_NetworkSend/NetworkSendDebugLayer.cs
```

核心编排逻辑：

```csharp
private IEnumerator RunPipeline()
{
    if (!RunHandshake())
    {
        return;
    }

    if (!RunRecordingStart())
    {
        return;
    }

    if (!RunCurrentSources())
    {
        return;
    }

    if (!RunQueues())
    {
        return;
    }

    if (!RunMerger())
    {
        return;
    }

    if (!RunLowFrequencyDebugImage())
    {
        return;
    }

    if (!RunDebugImagePacket())
    {
        return;
    }

    Debug.Log("SO debug chain passed through temporary Debug JPEG packet output.", this);
}
```

每个层级类只读对应层的 SO，并在失败时打印最小但完整的错误：

```csharp
private bool CheckTransmissionGate()
{
    if (transmissionGate.canEncodeAndSend)
    {
        Debug.Log("Transmission gate passed.", this);
        return true;
    }

    Debug.LogWarning(
        "Transmission gate blocked. " +
        "pc=" + transmissionGate.pcReceiverReady +
        " recording=" + transmissionGate.recordingActive +
        " synthesis=" + transmissionGate.synthesisHealthy +
        " blocker=" + transmissionGate.activeBlocker,
        this);

    return false;
}
```

## Debug 日志格式

建议统一格式，方便 Unity Console 和 Logcat 搜索：

```text
[SO-Debug][PASS][Handshake] phase=Paired host=... metadataPort=... videoPort=...
[SO-Debug][WAIT][Merger] mergedCount=12 missing=CameraImage latestDropReason=...
[SO-Debug][FAIL][TransmissionGate] pc=true recording=true synthesis=false blocker=...
```

规则：

- `PASS`：当前层已满足，准备进入下一层。
- `WAIT`：当前层还在等待，但未超时。
- `FAIL`：当前层超时或明确失败，停止后续判断。
- `ACTION`：调试器刚通过 SO 模拟了一次用户操作。

## 超时策略

每层都应该有独立超时，而不是整条链一个大超时。

| 层级 | 建议超时 |
|---|---|
| 网络握手 | 8 秒 |
| 录制进入 ShouldWriteQueues | 8 秒 |
| Current SO 新 frame | 3 秒 |
| Queue 增长 | 3 秒 |
| TimestampMerger sendable | 12 秒 |
| Transmission Gate | 3 秒 |
| Debug JPEG/MJPEG 编码 | 10 秒 |
| H264/H265 编码 | 15 秒 |
| Network packet | 5 秒 |

超时后只报当前层，不继续判断后面的层。

## 不应该继续保留的模式

以下模式应该停止扩张：

- 每个测试 case 一个 `RequestSO`。
- 每个测试 case 一个 `StateSO`。
- runner 自己维护 `phase`，再把正式链路 SO 状态转写进测试 SO。
- 把 H264/H265、MJPEG、图片链路混在同一个“编码测试成功/失败”里。
- 前面 gate 未通过时继续判断 encoder 或 network packet。

保留的最小资产应该是：

```text
SOFieldWriteRequest.asset
```

可选保留一个总摘要：

```text
SODebugProbeSummary.asset
```

这个摘要只记录：

```text
activeLayer
lastResult
lastMessage
lastBlocker
lastChangedUnixMs
```

它不能替代正式链路 SO，只能作为真机上快速看一眼的总结果。

## 实施顺序

1. 在对应 Runtime 子文件夹实现每层 `*DebugLayer.cs`，只引用正式链路 SO。
2. `DataCaptureSODebugPipeline.cs` 只做顺序集成，不承载各层具体诊断逻辑。
3. 用模拟输入 SO 或 Inspector/函数触发一次 debug run。
4. 每层失败立即停止后续层，输出 `PASS/WAIT/FAIL/ACTION`。
5. `SOStateViewer` 只需要显示正式 SO 和可选 `SODebugProbeSummary`。
6. 旧的 `DataCaptureSoDrivenAutoRecordingTest` 和 `DataCaptureSoDrivenEncodingSwitchTest` 不再继续扩展。
