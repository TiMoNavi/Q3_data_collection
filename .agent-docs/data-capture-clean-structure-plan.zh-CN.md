# DataCapture 干净结构规划

Last updated: 2026-06-12

## 目的

这份文档只规划结构，不立即移动文件或改场景引用。目标是把当前混在一起的采集、同步、编码、本地保存、网络发送、调试代码，整理成一条从上到下的单链路：

```text
00_SessionControl
  -> 10_CurrentSOInputs
  -> 20_QueueBuffers
  -> 30_TimeSynchronization
  -> 40_SingleEncodeProduction
  -> 50_ProductAssembly
  -> 60_Distribution
  -> 90_DebugAndTests
```

核心原则：

- Unity 场景层级、代码目录、SO 类型目录、SOData 实例目录使用同一套阶段编号和模块名。
- `Assets/SObasic/Runtime/ScriptableObjects/DataCapture` 只放 SO 类型定义。
- `Assets/SOData/DataCapture` 只放 SO 实例资源。
- `Assets/DataCapture` 放运行代码、工具代码和 Legacy。
- 旧结构逐步迁入 `Legacy`，不要继续在旧的 `50_EncodingNetwork`、`40_EncodingDecode` 等混合目录里扩展新功能。
- 编码层最终只读同步层输出的稳定 record，不再把 `CurrentVideoFrameInputSO` 当作正式编码输入契约。

Meta PCA 事实已核验：Quest Unity `PassthroughCameraAccess` 提供 live camera texture 等相机数据，不是现成编码流。来源：`https://developers.meta.com/horizon/llmstxt/documentation/unity/unity-pca-documentation.md`

## 目标代码目录

建议把新代码放在 `Assets/DataCapture/Runtime` 下，旧代码逐步移动到 `Assets/DataCapture/Legacy`。目录名和场景对象名保持一致：

```text
Assets/DataCapture/
  Runtime/
    00_SessionControl/
      ModeSelection/
      NetworkHandshake/
      RecordingState/
      StartStopControl/
    10_CurrentSOInputs/
      PassthroughCamera/
      VirtualLayer/
      Controller/
      Headset/
      CoordinateCalibration/
      NetworkDevice/
    20_QueueBuffers/
      Recorders/
      QueueAdapters/
    30_TimeSynchronization/
      Alignment/
      Merge/
      MetadataTimeline/
      Health/
      Export/
    40_SingleEncodeProduction/
      SynchronizedFrameReader/
      RenderTextureBuilder/
      EncoderBackends/
        AndroidMediaCodec/
        InstantReplayLocalPrototype/
      AccessUnitBus/
      Mp4Muxing/
      FrameIndex/
      Health/
    50_ProductAssembly/
      RealtimeStream/
      SessionArtifacts/
      Finalization/
    60_Distribution/
      RoutePolicy/
      LocalStore/
      LiveNetworkStream/
      SessionArtifactTransfer/
      Transports/
    90_DebugAndTests/
      StatusPreview/
      SOAccess/
      SmokeTests/
      Probes/
  Legacy/
    EncodingNetwork/
    EncodingDecode/
    NetworkSend/
    SoDrivenTests/
    MediaCodecSandbox/
```

当前代码迁移建议：

| 当前位置 | 目标位置 | 说明 |
|---|---|---|
| `CameraCapture/PassthroughCamera` | `Runtime/10_CurrentSOInputs/PassthroughCamera` | PCA current 写入层 |
| `CameraCapture/VirtualLayer` | `Runtime/10_CurrentSOInputs/VirtualLayer` | 虚拟图层是正式采集源 |
| `InputCapture/ControllerCapture` | `Runtime/10_CurrentSOInputs/Controller` | controller pose/button current 写入 |
| `InputCapture/HeadsetCapture` | `Runtime/10_CurrentSOInputs/Headset` | CenterEye pose current 写入 |
| `Coordinate system calibration` | `Runtime/10_CurrentSOInputs/CoordinateCalibration` | 建议去掉目录空格 |
| `Synchronization/Runtime/Sync` | `Runtime/30_TimeSynchronization/Merge` | 同步层主实现 |
| `Networking/Encoding/FrameSource` | `Runtime/40_SingleEncodeProduction/SynchronizedFrameReader` 或 `Legacy/EncodingNetwork` | `CurrentVideoFrameInput` 只能作为过渡 |
| `Networking/Encoding/OutputMode/LocalMp4Save` | `Runtime/40_SingleEncodeProduction/EncoderBackends/InstantReplayLocalPrototype` | 本地 MP4 已通，但不是最终总线 |
| `Networking/Encoding/OutputMode/SingleEncodeStreamAndMp4` | `Runtime/40_SingleEncodeProduction/EncoderBackends/AndroidMediaCodec` | 真实 texture bridge 完成后成为主线 |
| `Networking/Encoding/EncodedOutput` | `Runtime/50_ProductAssembly/SessionArtifacts` | 需要拆成实时流和完整 artifact |
| `Networking/Senders`、`Networking/Transport` | `Runtime/60_Distribution` | 分发层只消费 50 的产物 |
| `Testing/Runtime`、`Diagnostics` | `Runtime/90_DebugAndTests` | 诊断不混入生产链路 |

## SO 类型目录

SO 类型定义目录要和运行阶段一致：

```text
Assets/SObasic/Runtime/ScriptableObjects/DataCapture/
  00_SessionControl/
    SessionModeSO.cs
    RecordingSessionStateSO.cs
    RecordingToggleRequestSO.cs
    OutputRouteGateSO.cs
    PCDiscoveryRequestSO.cs
    PCReceiverConnectionStatusSO.cs
    RecordingExceptionLogSO.cs
  10_CurrentSOInputs/
    PassthroughCamera/
      CurrentCameraImageSO.cs
      CurrentCameraFrameTimingSO.cs
      CurrentCameraPoseSO.cs
      CurrentCameraMetadataSO.cs
      CurrentCameraStreamStateSO.cs
    VirtualLayer/
      CurrentVirtualLayerFrameSO.cs
    Controller/
      CurrentControllerPoseSO.cs
    Headset/
      CurrentHeadsetPoseSO.cs
    CoordinateCalibration/
      WorldCoordinateFrameSO.cs
      SessionCoordinateCalibrationSO.cs
      CoordinateCalibrationResetRequestSO.cs
    NetworkDevice/
      CurrentNetworkDeviceSO.cs
  20_QueueBuffers/
    PassthroughCamera/
      CameraImageQueueSO.cs
      CameraFrameTimingQueueSO.cs
      CameraPoseQueueSO.cs
      CameraMetadataQueueSO.cs
      CameraStreamStateQueueSO.cs
    VirtualLayer/
      VirtualLayerFrameQueueSO.cs
    Controller/
      ControllerPoseQueueSO.cs
    Headset/
      HeadsetPoseQueueSO.cs
    NetworkDevice/
      NetworkDeviceQueueSO.cs
  30_TimeSynchronization/
    SyncConfigurationSO.cs
    TimeStampVariable.cs
    CompositeAlignmentConfigurationSO.cs
    MergedFrameSnapshotQueueSO.cs
    MetadataTimelineJournalSO.cs
    SynchronizationHealthStateSO.cs
    TimestampMergerDebugStateSO.cs
  40_SingleEncodeProduction/
    EncodingPipelineConfigurationSO.cs
    EncoderConfigurationSO.cs
    EncodedAccessUnitQueueSO.cs
    CurrentEncodedAccessUnitSO.cs
    EncodingHealthStateSO.cs
    Mp4ArtifactWriterStateSO.cs
    FrameIndexSO.cs
  50_ProductAssembly/
    RealtimeAlignedStreamQueueSO.cs
    SessionArtifactManifestSO.cs
    SessionFinalizeStateSO.cs
  60_Distribution/
    NetworkSenderConfigurationSO.cs
    DistributionGateStateSO.cs
    CaptureTransmissionGateSO.cs
    CurrentNetworkPacketSO.cs
    NetworkPacketQueueSO.cs
    LocalSessionArtifactStoreStateSO.cs
    NetworkFramePacketConsumerStateSO.cs
    NetworkFileArtifactConsumerStateSO.cs
  90_DebugAndTests/
    QueueDebugStateSO.cs
    SOFieldWriteRequestSO.cs
    SORegistryListRequestSO.cs
    PassthroughStateData.cs
    Legacy/
```

命名规则：

- SO 类型文件保留 `SO` 后缀，例如 `SessionArtifactManifestSO.cs`。
- SO 实例资源不带 `SO` 后缀，例如 `SessionArtifactManifest.asset`。
- `Current*` 只表示最新值，不能跨阶段当作稳定输入契约。
- `*Queue` 只表示时间序列缓冲。
- `*Configuration` 是录制前配置。
- `*State` / `*HealthState` 是运行状态。
- `*Request` 是一次性请求。
- 不再使用 `EncodingNetwork` 这种混合名。

## SOData 实例目录

目标 SO 实例统一放在 `Assets/SOData/DataCapture`，并与场景层级同构：

```text
Assets/SOData/DataCapture/
  00_SessionControl/
    SessionMode.asset
    RecordingSessionState.asset
    RecordingToggleRequest.asset
    OutputRouteGate.asset
    PCDiscoveryRequest.asset
    PCReceiverConnectionStatus.asset
    RecordingExceptionLog.asset
  10_CurrentSOInputs/
    PassthroughCamera/
      CurrentCameraImage.asset
      CurrentCameraFrameTiming.asset
      CurrentCameraPose.asset
      CurrentCameraMetadata.asset
      CurrentCameraStreamState.asset
    VirtualLayer/
      CurrentVirtualLayerFrame.asset
    Controller/
      CurrentControllerPose.asset
    Headset/
      CurrentHeadsetPose.asset
    CoordinateCalibration/
      WorldCoordinateFrame.asset
      SessionCoordinateCalibration.asset
      CoordinateCalibrationResetRequest.asset
    NetworkDevice/
      CurrentNetworkDevice.asset
  20_QueueBuffers/
    PassthroughCamera/
      CameraImageQueue.asset
      CameraFrameTimingQueue.asset
      CameraPoseQueue.asset
      CameraMetadataQueue.asset
      CameraStreamStateQueue.asset
    VirtualLayer/
      VirtualLayerFrameQueue.asset
    Controller/
      ControllerPoseQueue.asset
    Headset/
      HeadsetPoseQueue.asset
    NetworkDevice/
      NetworkDeviceQueue.asset
  30_TimeSynchronization/
    SyncConfiguration.asset
    CurrentTimestamp.asset
    CompositeAlignmentConfiguration.asset
    MergedFrameSnapshotQueue.asset
    MetadataTimelineJournal.asset
    SynchronizationHealthState.asset
    TimestampMergerDebugState.asset
  40_SingleEncodeProduction/
    EncodingPipelineConfiguration.asset
    EncoderConfiguration.asset
    EncodedAccessUnitQueue.asset
    CurrentEncodedAccessUnit.asset
    EncodingHealthState.asset
    Mp4ArtifactWriterState.asset
    FrameIndex.asset
  50_ProductAssembly/
    RealtimeAlignedStreamQueue.asset
    SessionArtifactManifest.asset
    SessionFinalizeState.asset
  60_Distribution/
    NetworkSenderConfiguration.asset
    DistributionGateState.asset
    CaptureTransmissionGate.asset
    CurrentNetworkPacket.asset
    NetworkPacketQueue.asset
    LocalSessionArtifactStoreState.asset
    NetworkFramePacketConsumerState.asset
    NetworkFileArtifactConsumerState.asset
  90_DebugAndTests/
    QueueDebug_Camera.asset
    QueueDebug_VirtualLayer.asset
    QueueDebug_Controller.asset
    QueueDebug_Headset.asset
    SOFieldWriteRequest.asset
    SORegistryListRequest.asset
    PassthroughState.asset
    Legacy/
```

现有资源重命名建议：

| 当前资源 | 目标资源 |
|---|---|
| `00_Global/RecordingSessionState.asset` | `00_SessionControl/RecordingSessionState.asset` |
| `00_Global/RecordingToggleRequest.asset` | `00_SessionControl/RecordingToggleRequest.asset` |
| `00_Global/SyncConfiguration.asset` | `30_TimeSynchronization/SyncConfiguration.asset` |
| `00_Global/CurrentTimestamp.asset` | `30_TimeSynchronization/CurrentTimestamp.asset` |
| `50_EncodingNetwork/Network/Routing/OutputRouteGate.asset` | `00_SessionControl/OutputRouteGate.asset` |
| `50_EncodingNetwork/Network/Discovery/PCDiscoveryRequest.asset` | `00_SessionControl/PCDiscoveryRequest.asset` |
| `50_EncodingNetwork/Network/Discovery/PCReceiverConnectionStatus.asset` | `00_SessionControl/PCReceiverConnectionStatus.asset` |
| `10_CameraCapture/*` | `10_CurrentSOInputs/PassthroughCamera/Current*` 或 `20_QueueBuffers/PassthroughCamera/*Queue` |
| `20_VirtualLayerCapture/VirtualLayerQueue.asset` | `20_QueueBuffers/VirtualLayer/VirtualLayerFrameQueue.asset` |
| `30_PoseMetadataCapture/Controller/*` | `10_CurrentSOInputs/Controller/CurrentControllerPose.asset` 或 `20_QueueBuffers/Controller/ControllerPoseQueue.asset` |
| `40_MergedSynchronization/*` | `30_TimeSynchronization/*` |
| `50_EncodingNetwork/Encoding/RuntimeState/EncodedFrameQueue.asset` | `40_SingleEncodeProduction/EncodedAccessUnitQueue.asset` |
| `50_EncodingNetwork/Encoding/EncodedOutput/CaptureOutputQueue.asset` | 拆为 `50_ProductAssembly/RealtimeAlignedStreamQueue.asset` 和 `50_ProductAssembly/SessionArtifactManifest.asset` |
| `50_EncodingNetwork/Network/Transport/NetworkSenderConfiguration.asset` | `60_Distribution/NetworkSenderConfiguration.asset` |
| `50_EncodingNetwork/Diagnostics/TransmissionGate/CaptureTransmissionGate.asset` | 过渡期 `60_Distribution/CaptureTransmissionGate.asset`，最终收敛为 `DistributionGateState.asset` + `EncodingHealthState.asset` |
| `90_Diagnostics/Legacy/SoDrivenTests/*` | `90_DebugAndTests/Legacy/SoDrivenTests/*` |

## 目标 Unity 场景层级

外部 Meta/Unity building block 保持在场景根部：

```text
[BuildingBlock] Camera Rig
[BuildingBlock] Passthrough
[BuildingBlock] Passthrough Camera Access
DataCapture_Runtime
```

`DataCapture_Runtime` 建议整理成：

```text
DataCapture_Runtime
  00_SessionControl
    00_ModeSelection
    10_NetworkHandshakeIfNeeded
    20_RecordingInput
    30_RecordingState
    40_OutputRouteGate
  10_CurrentSOInputs
    00_PassthroughCamera_CurrentWriter
    10_VirtualLayer_CurrentWriter
      VirtualLayerCaptureCamera
    20_Controller_CurrentWriter
    30_Headset_CurrentWriter
    40_CoordinateCalibration
    50_NetworkDevice_CurrentWriter_OPTIONAL
  20_QueueBuffers
    00_PassthroughCamera_Queues
    10_VirtualLayer_Queue
    20_Controller_Queue
    30_Headset_Queue
    40_NetworkDevice_Queue_OPTIONAL
  30_TimeSynchronization
    00_TimestampMerger
    10_MetadataTimelineJournal
    20_SynchronizationHealth
    90_MergedSnapshotJsonExporter_DEBUG
  40_SingleEncodeProduction
    00_SynchronizedFrameReader
    10_SingleRenderTextureBuilder
    20_TextureToAccessUnitEncoder
    30_Mp4MuxerOrVideoArtifactWriter
    40_FrameIndexWriter
    50_EncodingHealth
  50_ProductAssembly
    00_RealtimeAlignedStreamQueueBuilder
    10_SessionArtifactManifestBuilder
    20_SessionFinalizeController
  60_Distribution
    00_RoutePolicy
    10_LocalSessionArtifactStore
    20_LiveNetworkStreamSender
    30_SessionArtifactFileTransfer
    40_Transports
      UdpTransport_Metadata
      UdpTransport_Video
  90_DebugAndTests
    00_StatusPreview
    10_SOAccessBridge
    20_SmokeTests
    90_LegacyTests_INACTIVE
```

### 00 Session Control 挂载

| GameObject | 组件 | 主要引用 |
|---|---|---|
| `00_ModeSelection` | `SessionModeController` 或当前过渡用 `OutputRouteGateController` | `SessionMode.asset`、`NetworkSenderConfiguration.asset`、`OutputRouteGate.asset` |
| `10_NetworkHandshakeIfNeeded` | `LanDiscoveryClient` | `NetworkSenderConfiguration.asset`、`PCDiscoveryRequest.asset`、`PCReceiverConnectionStatus.asset` |
| `20_RecordingInput` | `ControllerButtonRecordingToggleListener` | `CurrentControllerPose.asset`、`RecordingToggleRequest.asset` |
| `30_RecordingState` | `RecordingSessionController`、`RecordingToggleRequestConsumer` | `RecordingSessionState.asset`、`RecordingToggleRequest.asset`、`OutputRouteGate.asset`、所有 Queue 和 Recorder |
| `40_OutputRouteGate` | `OutputRouteGateController` | `NetworkSenderConfiguration.asset`、`PCReceiverConnectionStatus.asset`、`PCDiscoveryRequest.asset`、`OutputRouteGate.asset` |

说明：本地模式、联网握手、录制状态都在这里显式暴露。不要把 `CaptureTransmissionGateSO` 当作 00 启动门控。

### 10 Current SO Inputs 挂载

| GameObject | 组件 | 主要引用 |
|---|---|---|
| `00_PassthroughCamera_CurrentWriter` | `CameraPermissionRequest`、`PassthroughCameraAccessStartupGuard`、`PassthroughCameraFrameWriter` | `[BuildingBlock] Passthrough Camera Access`、五个 `CurrentCamera*` |
| `10_VirtualLayer_CurrentWriter` | `VirtualLayerFrameWriter`、`VirtualLayerCameraConfigurator` | `CurrentVirtualLayerFrame.asset`、`VirtualLayerCaptureCamera` |
| `VirtualLayerCaptureCamera` | `Camera`、`UniversalAdditionalCameraData` | 只作为虚拟图层采集相机，不作为同步后旁路入口 |
| `20_Controller_CurrentWriter` | `ControllerPoseCapture`、`ControllerButtonCapture` | controller anchors、`WorldCoordinateFrame.asset`、`SessionCoordinateCalibration.asset`、`CurrentControllerPose.asset` |
| `30_Headset_CurrentWriter` | `HeadsetPoseCapture` | `CenterEyeAnchor`、`WorldCoordinateFrame.asset`、`SessionCoordinateCalibration.asset`、`CurrentHeadsetPose.asset` |
| `40_CoordinateCalibration` | `CoordinateCalibrationController`、`CalibrationPoseCapture` | `RecordingSessionState.asset`、`WorldCoordinateFrame.asset`、`SessionCoordinateCalibration.asset`、`CoordinateCalibrationResetRequest.asset` |
| `50_NetworkDevice_CurrentWriter_OPTIONAL` | `NetworkDeviceReceiver`、`NetworkDeviceCurrentWriter`、`NetworkDeviceClockMapper` | `CurrentNetworkDevice.asset` |

说明：这一层只写 `Current*`，不写 Queue，不做同步，不做编码。

### 20 Queue Buffers 挂载

| GameObject | 组件 | 主要引用 |
|---|---|---|
| `00_PassthroughCamera_Queues` | 5 个 `CurrentToQueueRecorder` 或拆 5 个子对象 | `CurrentCameraImage -> CameraImageQueue`、`CurrentCameraFrameTiming -> CameraFrameTimingQueue`、`CurrentCameraPose -> CameraPoseQueue`、`CurrentCameraMetadata -> CameraMetadataQueue`、`CurrentCameraStreamState -> CameraStreamStateQueue`、`RecordingSessionState.asset` |
| `10_VirtualLayer_Queue` | `CurrentToQueueRecorder` | `CurrentVirtualLayerFrame.asset -> VirtualLayerFrameQueue.asset`、`RecordingSessionState.asset` |
| `20_Controller_Queue` | `CurrentToQueueRecorder` | `CurrentControllerPose.asset -> ControllerPoseQueue.asset`、`RecordingSessionState.asset` |
| `30_Headset_Queue` | `CurrentToQueueRecorder` | `CurrentHeadsetPose.asset -> HeadsetPoseQueue.asset`、`RecordingSessionState.asset` |
| `40_NetworkDevice_Queue_OPTIONAL` | `CurrentToQueueRecorder` | `CurrentNetworkDevice.asset -> NetworkDeviceQueue.asset`、`RecordingSessionState.asset` |

说明：虚拟图层必须进队列，再进同步层。不要从 `VirtualLayerFrameWriter` 直接连到编码或合成输出。

### 30 Time Synchronization 挂载

| GameObject | 组件 | 主要引用 |
|---|---|---|
| `00_TimestampMerger` | `TimestampMerger` | `CompositeAlignmentConfiguration.asset`、`RecordingSessionState.asset`、所有 required/optional queues、`MergedFrameSnapshotQueue.asset`、`TimestampMergerDebugState.asset` |
| `10_MetadataTimelineJournal` | `MetadataTimelineJournalWriter` | `MergedFrameSnapshotQueue.asset`、`MetadataTimelineJournal.asset` |
| `20_SynchronizationHealth` | `SynchronizationHealthReporter` | `TimestampMergerDebugState.asset`、`SynchronizationHealthState.asset` |
| `90_MergedSnapshotJsonExporter_DEBUG` | `MergedFrameSnapshotJsonExporter` | `MergedFrameSnapshotQueue.asset` |

说明：`MergedFrameSnapshotRecord` 是编码层唯一稳定输入契约。`30` 的输出顺序已经说明“哪个画面对应哪组 metadata”。

### 40 Single Encode Production 挂载

| GameObject | 组件 | 主要引用 |
|---|---|---|
| `00_SynchronizedFrameReader` | `SynchronizedFrameReader` | `MergedFrameSnapshotQueue.asset`、`MetadataTimelineJournal.asset` |
| `10_SingleRenderTextureBuilder` | `SingleRenderTextureBuilder`；过渡期可封装 `PassthroughCameraLayerCompositor` | 只读 `MergedFrameSnapshotRecord` 指向的稳定画面输入，不读会变化的 `CurrentVideoFrameInputSO` |
| `20_TextureToAccessUnitEncoder` | `AndroidMediaCodecAccessUnitEncoder` 或过渡期 smoke runner | `EncoderConfiguration.asset`、`EncodingPipelineConfiguration.asset`、`EncodedAccessUnitQueue.asset`、`EncodingHealthState.asset` |
| `30_Mp4MuxerOrVideoArtifactWriter` | `Mp4MuxerOrVideoArtifactWriter`；当前过渡用 `InstantReplayLocalMp4Recorder` | `EncodedAccessUnitQueue.asset`、`Mp4ArtifactWriterState.asset` |
| `40_FrameIndexWriter` | `FrameIndexWriter` | `MergedFrameSnapshotQueue.asset`、`MetadataTimelineJournal.asset`、`EncodedAccessUnitQueue.asset`、`FrameIndex.asset` |
| `50_EncodingHealth` | `EncodingHealthReporter` | `EncodingHealthState.asset`、`CaptureTransmissionGate.asset` 仅作后续发送诊断 |

说明：当前 `InstantReplayLocalMp4Recorder` 本地 MP4 已通，但它仍是原型 sink。最终目标是一条 `EncodedAccessUnitQueue` 同时喂实时流和 MP4。

### 50 Product Assembly 挂载

| GameObject | 组件 | 主要引用 |
|---|---|---|
| `00_RealtimeAlignedStreamQueueBuilder` | `RealtimeAlignedStreamQueueBuilder` | `EncodedAccessUnitQueue.asset`、`MetadataTimelineJournal.asset`、`SessionMode.asset`、`NetworkSenderConfiguration.asset`、`RealtimeAlignedStreamQueue.asset` |
| `10_SessionArtifactManifestBuilder` | `SessionArtifactManifestBuilder` | `Mp4ArtifactWriterState.asset`、`MetadataTimelineJournal.asset`、`FrameIndex.asset`、`EncodedAccessUnitQueue.asset`、`EncodingHealthState.asset`、`NetworkSenderConfiguration.asset`、`SessionArtifactManifest.asset` |
| `20_SessionFinalizeController` | `SessionFinalizeController` | `RecordingSessionState.asset`、`SessionMode.asset`、`NetworkSenderConfiguration.asset`、`SessionArtifactManifest.asset`、`MetadataTimelineJournal.asset`、`FrameIndex.asset`、`RealtimeAlignedStreamQueue.asset`、`EncodingHealthState.asset`、`Mp4ArtifactWriterState.asset`、`SessionFinalizeState.asset` |

说明：`50` 只组装产物，不做网络发送，不触发第二套渲染或编码。录制中由 `RealtimeAlignedStreamQueueBuilder` 在需要网络输出的 `outputTarget` 下产出流式传输用的 video+metadata 对齐记录；录制结束后由 `SessionArtifactManifestBuilder` 产出完整 MP4/metadata/frame-index manifest。`SessionFinalizeController` 按 `NetworkSenderConfiguration.outputTarget` 判定当前必须有哪些产物：`LocalFile` 只要求 MP4，`RemoteReceiver/SelfReceiver` 只要求 realtime stream，`RemoteAndLocalFile/SelfAndLocalFile` 两类都要求；缺失产物必须写入 blocker/异常信息。

### 60 Distribution 挂载

| GameObject | 组件 | 主要引用 |
|---|---|---|
| `00_RoutePolicy` | `DistributionRoutePolicy` | `SessionMode.asset`、`OutputRouteGate.asset`、`SessionFinalizeState.asset` |
| `10_LocalSessionArtifactStore` | `LocalSessionArtifactStore` | `SessionArtifactManifest.asset`、`LocalSessionArtifactStoreState.asset` |
| `20_LiveNetworkStreamSender` | `LiveNetworkStreamSender`；过渡期可复用 `MetadataPacketSender`、`VideoPacketSender` | `RealtimeAlignedStreamQueue.asset`、`NetworkSenderConfiguration.asset`、`NetworkFramePacketConsumerState.asset` |
| `30_SessionArtifactFileTransfer` | `SessionArtifactFileTransfer` | `SessionArtifactManifest.asset`、`NetworkFileArtifactConsumerState.asset` |
| `40_Transports/UdpTransport_Metadata` | `UdpPacketTransport` | `NetworkSenderConfiguration.asset` |
| `40_Transports/UdpTransport_Video` | `UdpPacketTransport` | `NetworkSenderConfiguration.asset` |

说明：`60` 只做分发。`LocalOnly` 只走本地完整 artifact；`NetworkOrHybrid` 录制中走实时流，停止后走完整 artifact file transfer。

### 90 Debug And Tests 挂载

| GameObject | 组件 | 主要引用 |
|---|---|---|
| `00_StatusPreview/QueueDebug_*` | `QueueDebugReporter` | 对应 queue asset、对应 `QueueDebugState.asset` |
| `00_StatusPreview/FrameTimingDebugReporter` | `FrameTimingDebugReporter`、`CaptureTransmissionGateReporter` | `MergedFrameSnapshotQueue.asset`、`TimestampMergerDebugState.asset`、`CaptureTransmissionGate.asset` |
| `00_StatusPreview/PassthroughStateCapture` | `PassthroughStateCapture` | `PassthroughState.asset` |
| `10_SOAccessBridge` | `SOFieldWriteRequestConsumer`、`SOFieldWriteRequestFileBridge`、`SORegistryListResponder`、`SOValueAccessController` | `SOFieldWriteRequest.asset`、`SORegistryListRequest.asset`、SO registry |
| `20_SmokeTests` | 当前 smoke runners | 只验证特定技术点，不作为生产入口 |
| `90_LegacyTests_INACTIVE` | 旧 SoDriven tests、MediaCodec sandbox | 默认 inactive |

说明：调试层可以读取全链路状态，但不要让调试组件成为生产链路必经节点。

## 当前场景对象迁移对照

| 当前对象 | 目标对象 |
|---|---|
| `00_Handshake_RecordingControl` | `00_SessionControl` |
| `10_CurrentSOInputs` | 保留为 `10_CurrentSOInputs`，内部重命名为更清晰模块 |
| `20_QueueBuffers` | 保留为 `20_QueueBuffers`，建议把 camera 五个 recorder 拆成可见子对象 |
| `30_Synchronization` | `30_TimeSynchronization` |
| `40_EncodingDecode` | 删除生产含义；Debug JPEG 移到 `90_DebugAndTests` |
| `50_EncodingNetwork/10_SharedVideoFrameInput` | 过渡期进入 `40_SingleEncodeProduction/00_SynchronizedFrameReader`；最终不读 Current |
| `50_EncodingNetwork/15_PassthroughUnityCompositor` | `40_SingleEncodeProduction/10_SingleRenderTextureBuilder` 的实现细节 |
| `50_EncodingNetwork/20_LocalMp4Save` | `40_SingleEncodeProduction/30_Mp4MuxerOrVideoArtifactWriter` 的过渡原型 |
| `50_EncodingNetwork/25_EncodedOutput` | 拆到 `50_ProductAssembly` |
| `50_NetworkSend` | `60_Distribution` |
| `80_StatusPreview` | `90_DebugAndTests/00_StatusPreview` |
| `90_AI_AutoDebug` | `90_DebugAndTests/10_SOAccessBridge`、`20_SmokeTests`、`90_LegacyTests_INACTIVE` |

## 迁移顺序建议

1. 先创建空的新目录和空场景父对象，不移动引用。
2. 用 Unity `AssetDatabase.MoveAsset` 或 Inspector 移动 SO 实例，保留 GUID，避免场景引用断掉。
3. 先迁移 `00 -> 30`，确认 Current、Queue、Sync 三段仍能跑。
4. 再把 `40` 重命名为 `SingleEncodeProduction`，把 Debug JPEG 从生产叙事中移走。
5. 最后拆 `50_ProductAssembly` 和 `60_Distribution`，不要在网络层里触发新的编码。
6. 全部迁移完成后，把旧目录移入 `Legacy` 并在场景中默认 inactive。

完成标准：

- Hierarchy 第一屏能看出本地/联网模式、录制状态、同步状态、编码状态、分发路线。
- 任意 SO asset 都能从路径判断它属于哪个阶段。
- 任意脚本都能从路径判断它应该挂在哪个场景对象下。
- 编码层没有正式组件直接读取会变化的 `CurrentVideoFrameInputSO`。
- 实时流和 MP4 都能追溯到同一批 `EncodedAccessUnitRecord`。
