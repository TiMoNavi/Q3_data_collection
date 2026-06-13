# 阶段 2：数据采集器

Last updated: 2026-06-10

## 范围

本文描述当前采集源和 Current SO 写入规则：

- Passthrough camera：拆成 5 组 Camera Current SO / Queue。
- 手柄：controller pose/buttons。
- 头显：只采集 CenterEyeAnchor。

重点结论：相机源参数来自 `PassthroughCameraAccess`，编码层不再假设固定分辨率/帧率，而是读取阶段 1 camera SO。

## Meta 官方 PCA 边界

已按 Quest 规则核验 Meta 官方文档：

- Unity PCA 通过 `PassthroughCameraAccess` 组件访问 camera data、intrinsics、extrinsics、timestamps。
- 当前项目使用：
  - `RequestedResolution`
  - `MaxFramerate`
  - `GetTexture()`
  - `CurrentResolution`
  - `Timestamp`
  - `Intrinsics`
  - `GetCameraPose()`
- 官方文档提示 passthrough camera 分辨率会随 Horizon OS/设备能力变化，应用不应假设固定分辨率。

Source: https://developers.meta.com/horizon/llmstxt/documentation/unity/unity-pca-overview.md

## 摄像头采集

### 场景源头

`Assets/Scenes/SampleScene.unity` 中的 `[BuildingBlock] Passthrough Camera Access` 是 PCA 分辨率、帧率、texture、intrinsics 和 camera pose 的源头。

当前目标场景配置方向：

```text
RequestedResolution: 1280x1280
MaxFramerate: 30
```

实际输出仍以运行时 `CurrentCameraStreamStateSO.currentResolution` 为准。

### 关键脚本

- `Assets/DataCapture/CameraCapture/PassthroughCamera/PassthroughCameraFrameWriter.cs`
- `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/10_CameraCapture/CurrentCameraImageSO.cs`
- `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/10_CameraCapture/CurrentCameraFrameTimingSO.cs`
- `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/10_CameraCapture/CurrentCameraPoseSO.cs`
- `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/10_CameraCapture/CurrentCameraMetadataSO.cs`
- `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/10_CameraCapture/CurrentCameraStreamStateSO.cs`
- `Assets/DataCapture/Synchronization/Runtime/DataTypes/CameraCaptureRecords.cs`

### Camera 5 组 Current SO

`PassthroughCameraFrameWriter` 每次 PCA 更新时写入 5 类记录：

```text
CurrentCameraImageSO
  currentTexture
  frameId
  timestampUnixMs / timestampUtc
  resolution
  encodedFrameId
  debugImagePath

CurrentCameraFrameTimingSO
  frameId
  timestampUnixMs
  timestampUtc
  unityFrame
  isUpdatedThisFrame

CurrentCameraPoseSO
  frameId
  timestampUnixMs
  cameraPosition
  cameraRotation
  cameraLocalToWorldMatrix
  cameraWorldToLocalMatrix

CurrentCameraMetadataSO
  frameId
  timestampUnixMs
  currentResolution
  focalLength
  principalPoint
  sensorResolution
  lensOffset
  projectionMatrix
  cameraLocalToWorldMatrix
  cameraWorldToLocalMatrix
  hasDistortionData / distortionCoefficients placeholder
  metadataSource

CurrentCameraStreamStateSO
  frameId
  timestampUnixMs
  cameraEye
  requestedResolution
  currentResolution
  requestedMaxFramerate
  measuredFramerate
  isPlaying
  isUpdatedThisFrame
  isSupported
  texturePropertyName
```

### Camera pose 来源

`CurrentCameraPoseSO` 现在写入的是：

```text
PassthroughCameraAccess.GetCameraPose()
```

这代表当前 RGB passthrough camera 的 pose，不再依赖 headset eye pose 近似。

### 编码层读取规则

阶段 4 的视频编码参数解析器读取：

```text
CurrentCameraStreamStateSO.currentResolution
CurrentCameraStreamStateSO.requestedMaxFramerate
CurrentCameraStreamStateSO.measuredFramerate
CurrentCameraImageSO.resolution
```

因此：

- camera source 分辨率/帧率由阶段 2 采集写入。
- `EncoderConfigurationSO` 表达编码目标和降采样策略。
- Debug JPEG 的低帧率/缩放由 `DebugImageStreamSettingsSO` 表达。

## 手柄采集

### 关键脚本

- `Assets/DataCapture/InputCapture/ControllerCapture/ControllerPoseCapture.cs`
- `Assets/DataCapture/InputCapture/ControllerCapture/ControllerButtonCapture.cs`
- `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/30_PoseMetadataCapture/Controller/CurrentControllerPoseSO.cs`
- `Assets/DataCapture/Synchronization/Runtime/DataTypes/ControllerPoseRecord.cs`

### 当前采集内容

`ControllerPoseCapture` 在 `LateUpdate()` 写入：

- 左右手世界坐标 position/rotation
- 左右手校准坐标 position/rotation
- `hasWorldCoordinateFrame`
- `hasCalibration`
- 坐标系名称

`ControllerButtonCapture` 在 `Update()` 写入：

- left trigger/grip/X/Y
- right trigger/grip/A/B

两个脚本写同一个 `CurrentControllerPoseSO`，通过先读 `currentPose.ToRecord()` 再覆盖部分字段来合并位姿和按键。

## 头显采集

### 关键脚本

- `Assets/DataCapture/InputCapture/HeadsetCapture/HeadsetPoseCapture.cs`
- `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/30_PoseMetadataCapture/Headset/CurrentHeadsetPoseSO.cs`
- `Assets/DataCapture/Synchronization/Runtime/DataTypes/HeadsetPoseRecord.cs`

### 当前采集内容

`HeadsetPoseCapture` 现在只采集：

- `centerEyeAnchor`
- `worldCenterEye`
- `calibratedCenterEye`
- `hasCenterEye`
- `hasWorldCoordinateFrame`
- `hasCalibration`
- 坐标系名称

旧 left/right eye 序列化字段已从 `CurrentHeadsetPose.asset` 清理。

头显 pose 仍对 XR telemetry 有价值，但它不再参与当前合成层 required alignment。

## 当前落盘方式

采集器本身不直接落盘。落盘发生在 Windows receiver 或后续本地视频保存链路：

- camera metadata 被合成到 `MergedMetadataPacket`，PC 侧写 `frames/frame_state.jsonl` 和 `frames/current_frame_state.json`。
- debug image payload 被 PC 侧写到 `frames/images/*.jpg`。
- 正式视频本地保存仍处于规划/骨架阶段。
