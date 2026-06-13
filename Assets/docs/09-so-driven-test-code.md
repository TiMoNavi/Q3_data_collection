# 09 SO 驱动测试代码与完整链路测试规划

Last updated: 2026-06-10

## 目标

本文规划“通过修改 SO 发起测试，通过读取 SO 状态判断链路”的运行时测试体系。

测试对象不是某一个组件，而是完整数据链路：

```text
阶段 0 网络握手
  -> 阶段 1 录制事件
  -> 阶段 2 数据捕捉 Current SO
  -> 阶段 3 Queue 缓冲与 TimestampMerger 同步
  -> 阶段 4 编码输出
  -> 阶段 5 网络发送
  -> 阶段 6 PC 端接收
```

核心原则：

- 测试入口用 SO，不直接调用正式链路组件方法。
- 测试结果也用 SO，不只看 Unity log 或 PC 文件。
- 每个阶段都要记录“输入、关键 SO 状态、通过条件、失败 blocker”。
- 图片链路、MJPEG fallback 视频链路、真实 H264/H265 MediaCodec 链路必须分开验证。
- 合并层、编码层、网络发送层的测试可以组合，但阶段判断不能混在一起。

## 当前已有测试

当前已有两类 SO 驱动测试：

```text
Assets/DataCapture/Testing/Runtime
  DataCaptureSoDrivenAutoRecordingTest.cs
  DataCaptureSoDrivenEncodingSwitchTest.cs
  SOFieldWriteRequestConsumer.cs
  SOFieldWriteRequestFileBridge.cs

Assets/SObasic/Runtime/ScriptableObjects/DataCapture/90_Diagnostics
  SoDrivenMergeLayerTestRequestSO.cs
  SoDrivenMergeLayerTestStateSO.cs
  SoDrivenEncodingSwitchTestRequestSO.cs
  SoDrivenEncodingSwitchTestStateSO.cs
  SOFieldWriteRequestSO.cs
  SoDrivenSixStagePipelineStatusSO.cs
```

规则：

- 运行时代码 runner / bridge 放在 `Assets/DataCapture/Testing/Runtime`。
- 所有 SO 类型定义放在 `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/...`。
- 所有 SO 实例 asset 放在 `Assets/SOData/DataCapture/...`。
- Unity 场景只引用 `SOData` 下的具体 asset 实例。

### DataCaptureSoDrivenAutoRecordingTest

职责：验证阶段 0 到阶段 3。

流程：

```text
PCDiscoveryRequestSO.Request(...)
  -> wait PCReceiverConnectionStatusSO.handshakeSucceeded
  -> RecordingToggleRequestSO.Request(...)
  -> wait RecordingSessionStateSO.ShouldWriteQueues
  -> wait TimestampMergerDebugStateSO.latestIsSendable
  -> RecordingToggleRequestSO.Request(stop)
```

该测试只证明可以从握手进入录制，并产出 sendable merged frame。它不验证编码，也不验证网络发送。

### DataCaptureSoDrivenEncodingSwitchTest

职责：验证阶段 4 的 debug 编码模式切换。

流程：

```text
等待 sendable merged frame
  -> DebugImageOnly + DebugJpeg      -> expect DEBUG_JPEG
  -> VideoOnly + DebugJpeg           -> expect DEBUG_MJPEG
  -> DebugImageAndVideo + DebugJpeg  -> expect DEBUG_JPEG + DEBUG_MJPEG
```

该测试只验证：

- Debug 图片输出。
- MJPEG fallback 视频产物。
- 同一套同步层结果可以切换成不同编码产物。

它不验证真实 H264/H265 MediaCodec 硬编输出。

## 需要新增的完整链路测试

现有两个测试不足以定位“为什么无法发帧或 H264”。需要新增完整链路测试：

```text
Assets/DataCapture/Testing/Runtime
  DataCaptureSoDrivenFullPipelineTest.cs

Assets/SObasic/Runtime/ScriptableObjects/DataCapture/90_Diagnostics
  SoDrivenFullPipelineTestRequestSO.cs
  SoDrivenFullPipelineTestStateSO.cs
  SoDrivenSixStagePipelineStatusSO.cs
```

建议保持在 `Assets/DataCapture/Testing/Runtime`，原因：

- 这是运行时测试基础设施，不属于正式采集、同步、编码或网络热路径。
- 可以被 Editor Play Mode 和 Quest 真机同时使用。
- 可以通过 ADB 文件桥触发，不依赖 Unity Inspector。

### SoDrivenFullPipelineTestRequestSO

完整链路测试入口。

建议字段：

```text
requested
requestRevision
requestedAtUnixMs
requestSource
testCase
requirePcHandshake
requireSendableMergedFrame
requireNetworkSend
requirePcReceiverEvidence
restorePipelineOnComplete
```

`testCase` 建议枚举：

```text
HandshakeOnly
MergeOnly
DebugImageOnly
DebugMjpegVideoOnly
AndroidMediaCodecH264
AndroidMediaCodecH265
DebugImageAndVideo
```

### SoDrivenSixStagePipelineStatusSO

这是排查链路的核心 SO，单独保存 6 个阶段是否正常。

类型定义：

```text
Assets/SObasic/Runtime/ScriptableObjects/DataCapture/90_Diagnostics/SoDrivenSixStagePipelineStatusSO.cs
```

实例 asset：

```text
Assets/SOData/DataCapture/90_Diagnostics/SoDrivenSixStagePipelineStatus.asset
```

6 个阶段固定为：

```text
0-1 Network handshake + recording gate
2 Current SO capture
3 Queue buffering
4 Timestamp synchronization
5 Encoding output
6 Network send + PC receive
```

每个阶段保存：

```text
stageName
state = NotStarted | Running | Passed | Failed | Skipped
passed
checkedAtUnixMs
statusMessage
blocker
frameId
timestampUnixMs
count
codec
byteLength
```

整体状态保存：

```text
isRunning
isComplete
hasFailure
activeStage
lastBlocker
statusMessage
startedAtUnixMs
updatedAtUnixMs
completedAtUnixMs
runRevision
recentEvents
```

这个 SO 应该被 `SOStateViewer` 注册到 diagnostics 组，也应该被完整链路测试 runner 引用。

### SoDrivenFullPipelineTestStateSO

完整链路测试状态输出。

建议字段：

```text
phase
isRunning
isComplete
hasFailure
statusMessage
lastBlocker
lastFailedStage
startedAtUnixMs
completedAtUnixMs

sixStageStatus
handshakeSnapshot
recordingSnapshot
currentSourceSnapshot
queueSnapshot
mergeSnapshot
transmissionGateSnapshot
encodingSnapshot
networkSendSnapshot
pcReceiverSnapshot

observedSourceCameraFrameId
observedMergedFrameId
observedEncodedFrameId
observedNetworkPacketFrameId
observedCodec
observedPayloadBytes
```

这个 SO 是测试的主看板。失败时应该能直接判断卡在：

```text
Handshake
RecordingStartGate
CurrentSources
QueueBuffers
TimestampMerger
TransmissionGate
Encoder
NetworkPacket
PcReceiver
```

`SoDrivenFullPipelineTestStateSO` 用于保存更详细的测试上下文；`SoDrivenSixStagePipelineStatusSO` 用于第一眼判断 6 个阶段是否正常。

不要把大量 payload 存进 SO。SO 里只放诊断摘要；图片、H264/H265 bytes、PC 接收文件仍由正式输出路径或 receiver 保存。

## 错误日志 SO

建议新增专门的测试日志 SO，而不是把所有错误塞进单个 state message：

```text
Assets/DataCapture/Testing/Runtime
  SoDrivenPipelineTestLogSO.cs
```

对应 asset：

```text
Assets/SOData/DataCapture/90_Diagnostics
  SoDrivenPipelineTestLog.asset
```

建议字段：

```text
lastError
lastWarning
lastInfo
lastChangedUnixMs
errorCount
warningCount
recentEvents[32]
```

用途：

- 记录测试 runner 的阶段切换和 blocker。
- 记录 SO 写入失败，例如 targetName 无法解析。
- 记录编码或网络发送失败摘要，例如 packet too large。
- 避免只依赖 Unity logcat；Quest 真机调试时可以通过状态 SO 汇总关键失败原因。

已有正式链路日志 SO 保持不变：

```text
RecordingExceptionLog.asset
CaptureTransmissionGate.asset
TimestampMergerDebugState.asset
PCReceiverConnectionStatus.asset
```

测试日志 SO 只记录测试过程，不替代正式链路 SO。

## SOData Asset 规划

测试相关 asset 统一放在：

```text
Assets/SOData/DataCapture/90_Diagnostics
  SOFieldWriteRequest.asset

  SoDrivenMergeLayerTestRequest.asset
  SoDrivenMergeLayerTestState.asset

  SoDrivenEncodingSwitchTestRequest.asset
  SoDrivenEncodingSwitchTestState.asset

  SoDrivenSixStagePipelineStatus.asset
  SoDrivenFullPipelineTestRequest.asset
  SoDrivenFullPipelineTestState.asset
  SoDrivenPipelineTestLog.asset
```

原因：

- 它们服务测试、诊断和 ADB 自动化。
- 不属于正式采集数据。
- 不应该混入 `00_Global`、`10_CameraCapture`、`50_EncodingNetwork` 等正式业务 SO 目录。

必须被 State Viewer 注册的测试 SO：

```text
SoDrivenMergeLayerTestRequest
SoDrivenMergeLayerTestState
SoDrivenEncodingSwitchTestRequest
SoDrivenEncodingSwitchTestState
SoDrivenSixStagePipelineStatus
SoDrivenFullPipelineTestRequest
SoDrivenFullPipelineTestState
SoDrivenPipelineTestLog
SOFieldWriteRequest
```

必须被 State Viewer 注册的正式链路 SO：

```text
PCDiscoveryRequest
PCReceiverConnectionStatus
NetworkSenderConfiguration

RecordingToggleRequest
RecordingSessionState
RecordingExceptionLog

CurrentCameraImage
CurrentCameraFrameTiming
CurrentCameraPose
CurrentCameraMetadata
CurrentCameraStreamState
CurrentControllerPose

CameraImageQueue
CameraFrameTimingQueue
CameraPoseQueue
CameraMetadataQueue
CameraStreamStateQueue
ControllerPoseQueue
MergedFrameSnapshotQueue

TimestampMergerDebugState
CaptureTransmissionGate

EncodingPipelineConfiguration
EncoderConfiguration
DebugImageStreamSettings
CurrentEncodedFrame
EncodedFrameQueue

CurrentNetworkPacket
NetworkPacketQueue
```

如果某个 SO 没有被 `SOStateViewer` 或 `SOFieldWriteRequestConsumer.writableAssets` 收录，ADB 的 `targetName` 写入可能失败。

## Unity 场景层级规划

`SampleScene` 的测试相关对象应挂在 `DataCapture_Runtime` 下，不散放在根节点。

推荐层级：

```text
DataCapture_Runtime
  00_Handshake_RecordingControl
  10_CurrentSOInputs
  20_QueueBuffers
  30_Synchronization
  40_EncodingDecode
  50_NetworkSend
  80_StatusPreview
    SOStateViewer
  90_AI_AutoDebug
    SO_WriteRequestBridge
      SOFieldWriteRequestConsumer
      SOFieldWriteRequestFileBridge
    Tests
      SO_Driven_MergeLayer_Test
        DataCaptureSoDrivenAutoRecordingTest
      SO_Driven_EncodingSwitch_Test
        DataCaptureSoDrivenEncodingSwitchTest
      SO_Driven_FullPipeline_Test
        DataCaptureSoDrivenFullPipelineTest
```

绑定规则：

- `SOStateViewer` 负责汇总可观察 SO。
- `SO_WriteRequestBridge` 负责把 ADB JSONL 写入转成运行时 SO 字段修改。
- `Tests` 只放测试 runner，不放正式采集、编码、发送组件。
- 测试 runner 只引用 SO，不直接引用 `LanDiscoveryClient`、`TimestampMerger`、`VideoPacketSender` 等热路径组件，除非只是读取只读诊断字段且没有等价 SO。

## ADB 自动化入口

Quest 真机运行时不能访问 Unity Inspector。自动化入口是文件桥：

```text
Application.persistentDataPath/DataCapture/so_write_requests.jsonl
```

Android 上通常是：

```text
/sdcard/Android/data/<package-name>/files/DataCapture/so_write_requests.jsonl
```

### 启动合并层测试

```json
{"source":"adb","targetName":"SoDrivenMergeLayerTestRequest","fieldPath":"requested","valueType":1,"boolValue":true}
```

### 启动编码切换测试

```json
{"source":"adb","targetName":"SoDrivenEncodingSwitchTestRequest","fieldPath":"requested","valueType":1,"boolValue":true}
```

### 启动完整链路测试

```json
{"source":"adb","targetName":"SoDrivenFullPipelineTestRequest","fieldPath":"testCase","valueType":7,"stringValue":"AndroidMediaCodecH264"}
{"source":"adb","targetName":"SoDrivenFullPipelineTestRequest","fieldPath":"requested","valueType":1,"boolValue":true}
```

`SOFieldWriteRequestFileBridge` 每帧消费一条 JSONL 命令，所以连续写多行不会被折叠成最后一个值。

## 完整链路测试流程

### 阶段 0：网络握手

触发：

```text
PCDiscoveryRequestSO.Request(...)
```

观察：

```text
PCReceiverConnectionStatusSO.phase
PCReceiverConnectionStatusSO.handshakeSucceeded
PCReceiverConnectionStatusSO.CanStartRecording
PCReceiverConnectionStatusSO.remoteHost
PCReceiverConnectionStatusSO.metadataPort
PCReceiverConnectionStatusSO.videoPort
PCReceiverConnectionStatusSO.lastStatusMessage
PCReceiverConnectionStatusSO.lastErrorMessage
PCReceiverConnectionStatusSO.lastBlocker
NetworkSenderConfigurationSO.remoteHost
NetworkSenderConfigurationSO.outputTarget
```

通过条件：

```text
handshakeSucceeded = true
CanStartRecording = true
remoteHost 非空，端口有效
```

失败 blocker 写入：

```text
SoDrivenFullPipelineTestState.handshakeSnapshot.blocker
SoDrivenPipelineTestLog.lastError
```

### 阶段 1：录制事件

触发：

```text
RecordingToggleRequestSO.Request(...)
```

或者模拟正式按钮链：

```text
CurrentControllerPoseSO.leftSecondaryButtonPressed = true
CurrentControllerPoseSO.leftSecondaryButtonPressed = false
```

观察：

```text
RecordingToggleRequestSO.requestRevision
RecordingSessionStateSO.State
RecordingSessionStateSO.ShouldWriteQueues
RecordingSessionStateSO.IsRecording
RecordingSessionStateSO.LastExceptionReason
RecordingSessionStateSO.LastDebugMessage
```

通过条件：

```text
State: NotStarted -> WarmingUp -> Recording
ShouldWriteQueues = true
```

这里验证第一道 gate：

```text
PCReceiverConnectionStatusSO.CanStartRecording
  -> RecordingSessionController
  -> RecordingSessionStateSO
```

### 阶段 2：Current SO 数据捕捉

观察 passthrough camera 5 组 Current SO：

```text
CurrentCameraImageSO.isValid
CurrentCameraImageSO.frameId
CurrentCameraImageSO.timestampUnixMs
CurrentCameraImageSO.resolution
CurrentCameraImageSO.currentTexture != null

CurrentCameraFrameTimingSO.isValid
CurrentCameraPoseSO.isValid
CurrentCameraMetadataSO.isValid
CurrentCameraStreamStateSO.isValid
CurrentCameraStreamStateSO.isPlaying
CurrentCameraStreamStateSO.isUpdatedThisFrame
CurrentCameraStreamStateSO.currentResolution
CurrentCameraStreamStateSO.requestedMaxFramerate
CurrentCameraStreamStateSO.measuredFramerate
```

观察 controller：

```text
CurrentControllerPoseSO.isValid
CurrentControllerPoseSO.timestampUnixMs
CurrentControllerPoseSO.recordSequence
```

通过条件：

```text
camera image/timing/pose/metadata/stream-state 均 valid
camera frameId 持续增长
currentTexture 非空
controller pose 至少能提供同步所需记录
```

### 阶段 3：Queue 缓冲

观察 required queue：

```text
CameraImageQueue.Count
CameraFrameTimingQueue.Count
CameraPoseQueue.Count
CameraMetadataQueue.Count
CameraStreamStateQueue.Count
ControllerPoseQueue.Count
```

同时观察 queue health：

```text
Capacity
OldestTimestamp
NewestTimestamp
OverwriteCount
LastClearTimestamp
GenerationId
```

通过条件：

```text
RecordingSessionStateSO.ShouldWriteQueues = true 后 required queue 开始增长
NewestTimestamp 在刷新
没有因 required queue 为空导致 merger 永久等待
```

### 阶段 4：TimestampMerger 同步

观察：

```text
TimestampMergerDebugStateSO.mergedCount
TimestampMergerDebugStateSO.latestCameraFrameId
TimestampMergerDebugStateSO.latestTimestampUnixMs
TimestampMergerDebugStateSO.latestStatus
TimestampMergerDebugStateSO.latestIsSendable
TimestampMergerDebugStateSO.latestDropReason
MergedFrameSnapshotQueueSO.Count
```

通过条件：

```text
latestStatus = Complete
latestIsSendable = true
MergedFrameSnapshotQueueSO 中出现 sendable snapshot
```

当前 required profile：

```text
CameraTiming
CameraImage
CameraPose
CameraMetadata
CameraStreamState
Controller
```

`VirtualLayer`、`Headset`、`NetworkDevice` 当前不应阻塞合成层发送。

### 阶段 5：发送 Gate

观察：

```text
CaptureTransmissionGateSO.pcReceiverReady
CaptureTransmissionGateSO.recordingActive
CaptureTransmissionGateSO.synthesisHealthy
CaptureTransmissionGateSO.canEncodeAndSend
CaptureTransmissionGateSO.activeBlocker
```

通过条件：

```text
pcReceiverReady = true
recordingActive = true
synthesisHealthy = true
canEncodeAndSend = true
```

这里验证第二道 gate：

```text
CaptureTransmissionGateSO.canEncodeAndSend =
  PCReceiverConnectionStatusSO.CanStartRecording
  && RecordingSessionStateSO.IsRecording
  && TimestampMergerDebugStateSO.latestIsSendable
  && required queue health OK
```

如果这道 gate 没开，编码和网络发送测试必须失败在 `TransmissionGate` 阶段，而不是继续误判为“encoder 没输出”。

### 阶段 6：编码输出

根据 test case 设置：

```text
DebugImageOnly:
  EncodingPipelineConfiguration.pipelineMode = DebugImageOnly
  videoEncoderBackend = DebugJpeg
  expect codec = DEBUG_JPEG

DebugMjpegVideoOnly:
  pipelineMode = VideoOnly
  videoEncoderBackend = DebugJpeg
  expect codec = DEBUG_MJPEG

AndroidMediaCodecH264:
  pipelineMode = VideoOnly
  videoEncoderBackend = AndroidMediaCodecH264
  expect codec = H264

AndroidMediaCodecH265:
  pipelineMode = VideoOnly
  videoEncoderBackend = AndroidMediaCodecH265
  expect codec = H265
```

观察：

```text
EncodingPipelineConfigurationSO.pipelineMode
EncodingPipelineConfigurationSO.videoEncoderBackend
EncodingPipelineDispatcher.lastDispatchStatus
AsyncDebugJpegNetworkStreamer.lastStreamerDebugMessage
AsyncMjpegVideoStreamEncoder.lastStatus
VideoStreamEncoderRunner.lastStatus
AndroidMediaCodecEncoderAdapter.lastStatus
CurrentEncodedFrameSO.isValid
CurrentEncodedFrameSO.codec
CurrentEncodedFrameSO.sourceCameraFrameId
CurrentEncodedFrameSO.byteLength
EncodedFrameQueueSO.Count
```

通过条件：

```text
CurrentEncodedFrameSO 或 EncodedFrameQueueSO 出现期望 codec
sourceCameraFrameId 大于测试开始前 baseline
byteLength > 0
```

真实 H264/H265 的阶段判定只要求拿到 MediaCodec 硬编 bytes。当前允许画面还是 GPU 测试图案，但必须是 `AndroidMediaCodecEncoderAdapter` 输出，不允许把 DEBUG_MJPEG 当成 H264/H265。

### 阶段 7：网络发送

观察：

```text
CurrentNetworkPacketSO
NetworkPacketQueueSO.Count
NetworkPacketQueueSO 最新 header frameId
NetworkPacketQueueSO 最新 header codec
NetworkPacketQueueSO 最新 header contentType
NetworkPacketQueueSO 最新 header payloadByteLength
```

通过条件：

```text
metadata packet 发送记录存在
video/debug packet 发送记录存在
packet codec 与编码阶段期望一致
```

注意：

- 当前 UDP 单包上限默认 `maxPacketBytes = 60000`。
- H264/H265 的 keyframe 可能超过单包上限。
- 如果编码阶段成功但网络发送失败，应标记为 `NetworkPacketTooLarge` 或 `NetworkFragmentationMissing`，不能回退判断为 encoder 失败。

### 阶段 8：PC 端接收

PC receiver 不在 Unity SO 系统内，但完整链路测试仍要定义外部证据。

建议 PC receiver 输出可被测试脚本或人工检查的证据：

```text
receiver log
metadata json
debug image jpg
video packet h264/h265/mjpeg
manifest / index jsonl
```

Unity 侧 state SO 只记录期望证据：

```text
expectedCodec
expectedSourceCameraFrameId
expectedEncodedFrameId
expectedPayloadBytes
expectedReceiverDirectory
```

真机自动化可以由外部脚本比对 PC receiver 文件，再把结果写回：

```text
SoDrivenFullPipelineTestState.pcReceiverSnapshot
```

短期也可以人工判断 PC 端文件是否出现，但文档和测试状态里必须把 PC 接收作为独立阶段。

## 推荐测试 Case

### Case A：HandshakeOnly

验证阶段 0。

用途：

- 网络环境排查。
- PC receiver 是否可发现。
- `NetworkSenderConfigurationSO` 是否被 discovery 正确写入。

### Case B：MergeOnly

验证阶段 0 到阶段 4。

用途：

- 确认录制事件、Current、Queue、TimestampMerger 都正常。
- 不进入编码和网络发送，降低变量。

### Case C：DebugImageOnly

验证阶段 0 到阶段 7，期望输出 `DEBUG_JPEG`。

用途：

- 快速确认图片链路。
- 验证 `CaptureTransmissionGateSO` 到 `VideoPacketSender` 的网络路径。

### Case D：DebugMjpegVideoOnly

验证阶段 0 到阶段 7，期望输出 `DEBUG_MJPEG`。

用途：

- 证明同一套同步层可以输出视频类产物。
- 验证 `EncodingPipelineConfigurationSO.pipelineMode = VideoOnly` 的切换。

### Case E：AndroidMediaCodecH264

验证阶段 0 到阶段 7，期望输出 `H264`。

用途：

- 证明 Unity 真机上能通过 Android MediaCodec 硬编拿到 bytes。
- 当前阶段允许画面是 GPU 测试图案。
- 如果网络发送失败，要区分是 packet 太大还是 encoder 没输出。

### Case F：AndroidMediaCodecH265

同 Case E，期望输出 `H265`。

## 场景绑定检查清单

进入真机测试前，Unity 场景必须检查：

```text
SOStateViewer
  包含所有 request/state/log SO
  包含所有阶段观测 SO

SOFieldWriteRequestConsumer
  request = SOFieldWriteRequest.asset
  stateViewerRegistry = SOStateViewer
  writableAssets 可为空，但 SOStateViewer 必须注册完整

SOFieldWriteRequestFileBridge
  request = SOFieldWriteRequest.asset
  relativeCommandPath = DataCapture/so_write_requests.jsonl

DataCaptureSoDrivenFullPipelineTest
  request = SoDrivenFullPipelineTestRequest.asset
  state = SoDrivenFullPipelineTestState.asset
  sixStageStatus = SoDrivenSixStagePipelineStatus.asset
  log = SoDrivenPipelineTestLog.asset
  绑定所有正式链路 SO
```

正式链路组件必须仍在自己的层级：

```text
00_Handshake_RecordingControl
10_CurrentSOInputs
20_QueueBuffers
30_Synchronization
40_EncodingDecode
50_NetworkSend
```

测试组件不得移动正式链路组件，也不应该把正式 sender 或 encoder 放进 `90_AI_AutoDebug`。

## 失败定位规则

失败时按阶段向前定位，不跳级：

```text
PCReceiverConnectionStatusSO.handshakeSucceeded = false
  -> 阶段 0 失败

RecordingSessionStateSO.ShouldWriteQueues = false
  -> 阶段 1 失败

CurrentCameraImageSO.currentTexture = null
  -> 阶段 2 失败

CameraFrameTimingQueue.Count = 0
  -> 阶段 3 失败

TimestampMergerDebugStateSO.latestIsSendable = false
  -> 阶段 4 失败

CaptureTransmissionGateSO.canEncodeAndSend = false
  -> 阶段 5 失败

CurrentEncodedFrameSO.codec != expectedCodec
  -> 阶段 6 失败

NetworkPacketQueueSO 没有 expected codec packet
  -> 阶段 7 失败

PC receiver 没有对应文件
  -> 阶段 8 失败
```

这条规则可以避免把场景绑定、recording gate、transmission gate、packet size、receiver 问题误判成 H264/H265 编码问题。

## 当前限制

- `DataCaptureSoDrivenEncodingSwitchTest` 当前只验证 DEBUG_JPEG 和 DEBUG_MJPEG，不验证真实 H264/H265。
- `SelfReceiver` 目前只是网络配置方向，Quest 内本机 receiver/reassembler 仍待实现。
- `LocalFile` 目前只是保存配置方向，正式 MP4 writer/muxer 仍待实现。
- H264/H265 网络发送还缺分片协议，单包 UDP 只适合小 payload。
- Quest 运行时不能访问 Unity Inspector，因此真机自动化必须走 ADB 文件桥或外部测试脚本。
- PC receiver 证据目前不属于 Unity SO，需要外部脚本或人工检查后再回写测试结果。

## 实施顺序

1. 新增完整链路 request/state/log SO。
2. 新增 `DataCaptureSoDrivenFullPipelineTest`，只读/写 SO，不直接改热路径。
3. 在 `SOData/DataCapture/90_Diagnostics` 创建对应 asset。
4. 在 `DataCapture_Runtime/90_AI_AutoDebug/Tests` 下挂测试对象并绑定 SO。
5. 把新增测试 SO 和关键正式 SO 注册进 `SOStateViewer`。
6. 先跑 `HandshakeOnly`、`MergeOnly`。
7. 再跑 `DebugImageOnly`、`DebugMjpegVideoOnly`。
8. 最后跑 `AndroidMediaCodecH264/H265`，并把编码成功、网络发送失败、PC 接收失败分开记录。
