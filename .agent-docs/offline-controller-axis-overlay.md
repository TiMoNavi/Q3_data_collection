# PC 离线绘制手柄 XYZ 轴 Overlay

Last updated: 2026-06-09

## 结论

现有 PC 接收目录已经足够做离线手柄轴绘制。每个完整 session 同时保存了：

- `metadata/merged_metadata.jsonl`：Quest 端发送的合并 metadata。
- `video/video_headers.jsonl`：每个 debug image payload 对应的 header。
- `video/frames/`：PC 接收到的低频 `DEBUG_JPEG` 帧。

绘制时必须使用 `snapshot.camera` 中来自 Quest Passthrough Camera API 的相机位姿、内参和矩阵。不要用 `headset.worldCenterEye` 或 `CenterEyeAnchor` 当作投影相机。

当前图片实际是 `DEBUG_JPEG`，不是 PNG。离线工具同时支持 `.jpg`、`.jpeg`、`.png`。

## 官方与本地依据

Meta 当前 Unity Passthrough Camera API 文档确认 `PassthroughCameraAccess` 可提供 `Timestamp`、`GetCameraPose()`、`Intrinsics`、`CurrentResolution`，并提供 world/viewport 相关转换能力：

`https://developers.meta.com/horizon/llmstxt/documentation/unity/unity-pca-migration-from-webcamtexture.md`

本项目中相机 metadata 的来源是：

- `Assets/DataCapture/CameraCapture/PassthroughCamera/PassthroughCameraFrameWriter.cs`
- 该脚本从 `PassthroughCameraAccess.Timestamp`、`GetCameraPose()`、`Intrinsics`、`CurrentResolution` 写入 `CameraFrameRecord`。
- 写入字段包括 `cameraPosition`、`cameraRotation`、`focalLength`、`principalPoint`、`sensorResolution`、`projectionMatrix`、`cameraLocalToWorldMatrix`、`cameraWorldToLocalMatrix`。

本项目中手柄 world pose 的来源是：

- `Assets/DataCapture/Runtime/10_CurrentSOInputs/Controller/ControllerPoseCapture.cs`
- 优先读取 `leftHandAnchor.position/rotation` 和 `rightHandAnchor.position/rotation`。
- `Assets/Scenes/SampleScene.unity` 中 `ControllerPoseCapture` 已绑定 `LeftControllerAnchor` 和 `RightControllerAnchor`。

如果采集时手柄没有启动，metadata 中 `worldLeftPosition/worldRightPosition` 可能是 `(0,0,0)`，rotation 可能是 identity。这种记录应视为无效手柄位姿，不应回退到 calibrated pose。

## 数据读取

PCReceiver 保存的 metadata 每行是外层 JSON，真正的 Quest payload 在字符串字段 `payload` 中，需要二次解析：

```text
outer = json.loads(line)
payload = json.loads(outer["payload"])
snapshot = payload["snapshot"]
```

视频 header 每行可读取：

```text
video.header.sourceCameraFrameId
video.header.header.timestampUnixMs
video.absolutePath 或 video.file
```

离线匹配规则：

1. 读取全部 `metadata/merged_metadata.jsonl`。
2. 建立 `snapshot.frameId` 和 `snapshot.camera.frameId` 到 `snapshot` 的索引。
3. 读取 `video/video_headers.jsonl`。
4. 用 `video.header.sourceCameraFrameId` 找到同一个 camera frame 的 `snapshot`。
5. 对匹配到的图像执行投影绘制。

## 投影算法

默认只绘制 world-space controller pose：

```text
P = controller.worldLeftPosition 或 controller.worldRightPosition
R = controller.worldLeftRotation 或 controller.worldRightRotation
axisLength = 0.12 m

X = P + R * (axisLength, 0, 0)
Y = P + R * (0, axisLength, 0)
Z = P + R * (0, 0, axisLength)
```

每个 world point 先用 `snapshot.camera.cameraWorldToLocalMatrix` 转到相机局部坐标：

```text
cameraLocal = cameraWorldToLocalMatrix * worldPoint
```

若 `cameraLocal.z <= 0`，该点在相机后方，跳过该点对应的轴；若 pivot 在相机后方，跳过该手柄。

再用 PCA camera intrinsics 投影：

```text
sensorU = cameraLocal.x / cameraLocal.z * focalLength.x + principalPoint.x
sensorV = cameraLocal.y / cameraLocal.z * focalLength.y + principalPoint.y
```

工具会按项目里 `PassthroughProjectionUtility.CalculateSensorCropRegion` 的逻辑处理 `sensorResolution` 和 `resolution` 的 crop，然后得到 normalized viewport：

```text
viewportX = (sensorU - crop.x) / crop.width
viewportY = (sensorV - crop.y) / crop.height
```

最后映射到保存图片的真实尺寸，并把 Y 轴转为图片 top-left convention：

```text
pixelX = viewportX * imageWidth
pixelY = (1 - viewportY) * imageHeight
```

因为当前 debug image 是从原始 PCA texture 降采样得到的，例如 `1280x1280 -> 320x320`，所以不要直接把 sensor pixel 当作保存图片 pixel。应先得到 viewport，再按实际图片大小缩放。

## 工具

脚本路径：

```powershell
PCReceiver/offline_controller_axis_overlay.py
```

对最新完整 session 做 dry-run：

```powershell
python .\PCReceiver\offline_controller_axis_overlay.py --dry-run --verbose
```

对指定 session 做 dry-run：

```powershell
python .\PCReceiver\offline_controller_axis_overlay.py `
  .\captures\q3_full_chain_rerun_20260609_124103\q3dc_session_20260609_124156 `
  --dry-run --verbose
```

生成 overlay 图片：

```powershell
python .\PCReceiver\offline_controller_axis_overlay.py `
  .\captures\q3_full_chain_rerun_20260609_124103\q3dc_session_20260609_124156
```

默认输出目录：

```text
<session>/video/frames_overlay/
```

默认只在至少画出一根轴时写图。如果希望没有有效手柄位姿的帧也按原图写到 overlay 目录，用：

```powershell
python .\PCReceiver\offline_controller_axis_overlay.py `
  .\captures\...\q3dc_session_... `
  --write-empty
```

默认绘制约定：

- X 轴：红色。
- Y 轴：绿色。
- Z 轴：蓝色。
- 左手柄标注 `L`。
- 右手柄标注 `R`。
- 轴长：`0.12m`。
- 线宽：`3px`。

可调参数：

```powershell
python .\PCReceiver\offline_controller_axis_overlay.py `
  .\captures\...\q3dc_session_... `
  --axis-length 0.16 `
  --line-width 4 `
  --output-dir .\captures\...\q3dc_session_...\video\frames_overlay
```

## 验收口径

dry-run 应输出：

- `metadata_records`：metadata 中可解析的 frame 数。
- `video_records`：可解析的 image header 数。
- `matched_frames`：能用 `sourceCameraFrameId` 找到 metadata 的 image 数。
- `timestamp_warnings`：video timestamp 与 metadata camera timestamp 差值超过阈值的数量。
- `skipped.*_controller_world_pose_invalid`：手柄未启动或 world pose 为零值时的跳过数量。

当前已有两组完整采集里，手柄采集时未启动 controller，因此 world controller pose 是零值。预期结果是：

- 能成功解析 metadata、相机位姿和图像 header。
- 能匹配 video frame 与 metadata。
- 不绘制手柄轴。
- 输出 `left_controller_world_pose_invalid` / `right_controller_world_pose_invalid` 诊断。

正式验证需要重新采集一组手柄已启动的数据。验收标准：

- `matched_frames == video_records`。
- `timestamp_warnings == 0` 或仅有可解释的小量 warning。
- `axes_drawn > 0`。
- 输出图片中左右手柄 pivot 上出现红、绿、蓝三轴。
- 转动手柄时轴方向跟随手柄旋转。
- 视觉上没有使用 headset center eye 当作相机产生的明显视差错误。

## 当前边界

- 默认不使用 calibrated pose，避免把 world-camera 投影和 session-calibrated 坐标混在一起。
- 默认不处理 360/equirectangular 投影；当前数据按 Quest PCA pinhole camera intrinsics 处理。
- 当前视频路径是低频 `DEBUG_JPEG`，用于链路和时间戳验证，不代表正式 H.264/H.265 视频流。
