# 08 运行时组装流程

Last updated: 2026-06-10

## 目标

`SampleScene` 的 `DataCapture_Runtime` 现在按采集操作逻辑编排，而不是按脚本目录临时堆放。

主流程：

```text
1. 网络握手
2. 录制按钮与录制状态控制
3. 各输入源统一写入 Current SO
4. Current SO 写入 Queue 缓冲区
5. TimestampMerger 同步合成
6. 编码/解码处理层
7. 网络发送层
8. 状态预览
9. AI/ADB 自动调试
```

原则：
- 输入源只负责从 OVR/PCA/虚拟层/占位网络设备等渠道写 Current SO。
- 队列层只负责 Current -> Queue，不再混放 controller/headset 采集脚本。
- 合成层只负责同步合成，不承载录制按钮入口。
- 编码层和网络发送层分离。
- AI/ADB 调试入口不混入正式采集热路径。

## 场景根结构

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
```

## 00_Handshake_RecordingControl

```text
00_Handshake_RecordingControl
  LanDiscoveryClient
    LanDiscoveryClient
  RecordingButton_SOListener
    ControllerButtonRecordingToggleListener
  RecordingSessionController
    RecordingSessionController
    RecordingToggleRequestConsumer
```

职责：
- `LanDiscoveryClient` 监听 `PCDiscoveryRequestSO`，完成局域网 discovery/handshake。
- `RecordingButton_SOListener` 使用 `ControllerButtonRecordingToggleListener`，只读 `CurrentControllerPoseSO` 中配置的录制按钮状态，默认绑定 `LeftSecondaryButton`（Y）。
- Execution order note: `ControllerButtonRecordingToggleListener` runs before live controller capture so Inspector/AI/ADB writes to the controller button SO can trigger before `ControllerButtonCapture` refreshes hardware state.
- `RecordingToggleRequestConsumer` 消费 `RecordingToggleRequestSO`。
- `RecordingSessionController` 推进 `RecordingSessionStateSO`。

关键 SO：
- `PCDiscoveryRequest.asset`
- `PCReceiverConnectionStatus.asset`
- `RecordingToggleRequest.asset`
- `RecordingSessionState.asset`
- `RecordingExceptionLog.asset`

## 10_CurrentSOInputs

```text
10_CurrentSOInputs
  00_Headset_OVR_CurrentWriter
    HeadsetPoseCapture
  10_Controller_OVR_CurrentWriter
    ControllerPoseCapture
    ControllerButtonCapture
  20_Camera_Passthrough_CurrentWriter
    PassthroughCameraFrameWriter
  30_VirtualLayer_CurrentWriter
    VirtualLayerFrameWriter
  40_NetworkDevice_CurrentWriter_PLACEHOLDER
  50_CoordinateCalibration
    CoordinateCalibrationController
    CalibrationPoseCapture
```

职责：
- 从 OVR / Meta XR rig、PCA BuildingBlock、虚拟层、占位网络设备等源头采集数据。
- 统一写入 Current SO 系统。
- 不直接写 Queue。

当前输入源：
- Headset: `CurrentHeadsetPoseSO`
- Controller pose/buttons: `CurrentControllerPoseSO`
- Passthrough camera: camera image/timing/pose/metadata/stream-state 5 组 Current SO
- Virtual layer: `CurrentVirtualLayerFrameSO`
- Network device: 当前只保留 placeholder
- Coordinate calibration: 写坐标系校准 SO

## 20_QueueBuffers

```text
20_QueueBuffers
  Recorder_CameraFrame
    CurrentToQueueRecorder x5
  Recorder_VirtualLayerFrame
    CurrentToQueueRecorder
  Recorder_ControllerPose
    CurrentToQueueRecorder
  Recorder_HeadsetPose
    CurrentToQueueRecorder
  Recorder_NetworkDevice_PLACEHOLDER
    CurrentToQueueRecorder
```

职责：
- 只做 Current SO -> Queue SO。
- 受 `RecordingSessionStateSO.ShouldWriteQueues` 控制。
- 不再混放 `ControllerPoseCapture`、`ControllerButtonCapture`、`HeadsetPoseCapture`。

## 30_Synchronization

```text
30_Synchronization
  TimestampMerger
    TimestampMerger
  MergedSnapshotJsonExporter
    MergedFrameSnapshotJsonExporter
```

职责：
- 以 camera timing 为主时钟，从各 Queue 中按 tolerance 取最近数据。
- 产出 `MergedFrameSnapshotQueueSO`。
- 写 `TimestampMergerDebugStateSO`。

本层不处理录制按钮入口，不处理网络发送。

## 40_EncodingDecode

```text
40_EncodingDecode
  00_DebugImageEncoding_DEBUG_ONLY
    AsyncDebugJpegNetworkStreamer_DEBUG_ONLY
      AsyncDebugJpegNetworkStreamer
  10_VideoEncoding_PLACEHOLDER
    EncodedFrameQueueWriter_PLACEHOLDER_H264_H265
```

职责：
- Debug JPEG 当前只是低帧率调试图像链路。
- 正式 H.264/H.265/MediaCodec 入口放在 `10_VideoEncoding_PLACEHOLDER` 下继续补。
- 本层产出 encoded frame，不负责网络发送。

命名说明：
- 这里沿用 `EncodingDecode` 是为了给后续 decoder/reassembler 本机验证留空间。
- 当前真正运行的是 encoding/debug image，不是正式视频解码。

## 50_NetworkSend

```text
50_NetworkSend
  00_Transports
    UdpTransport_Metadata
      UdpPacketTransport
    UdpTransport_Video
      UdpPacketTransport
  10_PacketSenders
    MetadataPacketSender
      MetadataPacketSender
    VideoPacketSender
      VideoPacketSender
  20_Coordination
    NetworkTransmissionCoordinator
      NetworkTransmissionCoordinator
```

职责：
- 统一承载网络发送层。
- transport、packet sender、send coordinator 分开。
- 未来做 video fragment sender / self receiver / local file writer 时，应继续放在本层或与本层平级拆出保存层。

## 80_StatusPreview

```text
80_StatusPreview
  QueueDebug_Camera
  QueueDebug_VirtualLayer
  QueueDebug_Controller
  QueueDebug_Headset
  FrameTimingDebugReporter
    FrameTimingDebugReporter
    CaptureTransmissionGateReporter
  PassthroughStateCapture
  SOStateViewer
```

职责：
- 给人看状态。
- 汇总 Queue health、合成状态、发送闸门、passthrough visibility、SOData。
- State Viewer 可在 Unity Editor Inspector 中查看/修改 SO。

注意：
- State Viewer 是 Editor/Inspector 工具。
- Quest 运行时没有 Unity Inspector 面板。

## 90_AI_AutoDebug

```text
90_AI_AutoDebug
  SO_WriteRequestBridge
    SOFieldWriteRequestConsumer
    SOFieldWriteRequestFileBridge
  Tests
    SO_Driven_MergeLayer_Test
      DataCaptureSoDrivenAutoRecordingTest
```

职责：
- 给 AI/ADB 自动调试留入口。
- `SOFieldWriteRequestFileBridge` 轮询 `Application.persistentDataPath/DataCapture/so_write_requests.jsonl`。
- `SOFieldWriteRequestConsumer` 把文件/Inspector 写入转换成运行时 SO 字段修改。
- `DataCaptureSoDrivenAutoRecordingTest` 通过修改 SO 跑到合并层输出后停止。

关键 SO：
- `SOFieldWriteRequest.asset`
- `SoDrivenMergeLayerTestRequest.asset`
- `SoDrivenMergeLayerTestState.asset`

## SOData 目录划分

```text
Assets/SOData/DataCapture
  00_Global
    CurrentTimestamp.asset
    RecordingExceptionLog.asset
    RecordingSessionState.asset
    RecordingToggleRequest.asset
    SyncConfiguration.asset
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

补齐说明：
- `RecordingSessionState.asset.exceptionLog` 已绑定 `RecordingExceptionLog.asset`。
- `PassthroughState.asset` 对应 `PassthroughStateData`，由 `PassthroughStateCapture` 写入。
- `SoDrivenMergeLayerTestRequest/State` 与 `SOFieldWriteRequest` 放在 `90_Diagnostics`，因为它们服务调试和测试。

## 旧错误资产扫描结论

本轮扫描未发现以下旧链路资产残留在 `Assets/SOData`：
- `RecordingGate`
- `DataCaptureTestAutoStartRecording`
- `ControllerYButtonDiscoveryRequestListener`
- 旧单体 `CurrentCameraFrameSO`
- 旧单体 `CameraFrameQueueSO`

当前出现的 `CurrentCameraFrameTiming` 和 `CameraFrameTimingQueue` 是 camera 5 组拆分后的 timing 资产，不是旧错误资产。

当前 placeholder：
- `40_NetworkDevice_CurrentWriter_PLACEHOLDER`
- `Recorder_NetworkDevice_PLACEHOLDER`
- `10_VideoEncoding_PLACEHOLDER`

这些不是错误 asset，但不应被误认为当前 PCA 数据集 required path。
