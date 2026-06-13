# 阶段 6：Windows 接收器

Last updated: 2026-06-09

## 范围

本文描述 Windows 侧 receiver 如何响应 discovery、接收 metadata/debug video、创建 session、落盘和 GUI 预览。

当前接收器是 Python UDP receiver，入口有 CLI 和 Tk GUI 两种：

- `PCReceiver/q3dc_receiver.py`
- `PCReceiver/q3dc_receiver_gui.py`

## 关键模块

### q3dc_receiver.py

核心类：

- `Receiver`
- `ExportOptions`

核心线程：

- `discovery_loop`
- `metadata_loop`
- `video_loop`

辅助函数：

- `parse_metadata_payload`
- `compact_frame_state`
- `parse_video_payload`
- `video_filename`
- `write_json`

### q3dc_receiver_gui.py

GUI 包装：

- `ReceiverWindow`
- 启动/停止 receiver thread
- 展示 discovery/metadata/video 计数
- 展示 metadata summary
- 展示最新 JPEG preview

## 端口与协议

默认端口：

```text
discovery: 49000
metadata: 5001
video: 5000
```

Discovery 请求：

```text
Q3DC_DISCOVER
```

Discovery 响应 JSON：

```json
{
  "magic": "Q3DC",
  "protocolVersion": 1,
  "deviceRole": "pc_receiver",
  "receiverHost": "...",
  "metadataPort": 5001,
  "videoPort": 5000,
  "features": ["metadata", "debug_video"]
}
```

Video envelope magic：

```text
Q3DCBIN1
```

## Session 创建规则

GUI 默认 `lazy_session = true`。

这意味着：

1. 启动 receiver 时只创建 `_receiver/discovery` 和 `_receiver/logs`。
2. 不立即创建正式采集 session。
3. 第一次收到 headset metadata 或 video/debug packet 时，调用：

```text
ensure_session(trigger, remote)
```

4. 创建：

```text
q3dc_session_YYYYMMDD_HHMMSS/
```

这符合当前测试设计：session 由头显开始录制触发，而不是由 PC receiver 启动触发。

## 落盘结构

当前 `q3dc_receiver.py` 的新布局：

```text
q3dc_session_YYYYMMDD_HHMMSS/
  session_manifest.json
  global_status.json
  discovery/
    discovery_events.jsonl
  frames/
    current_frame_state.json
    frame_state.jsonl
    images/
      frame_000031.jpg
  logs/
    receiver.log
```

注意：旧测试目录中可能还有：

```text
video/video_headers.jsonl
video/frames/*.jpg
metadata/merged_metadata.jsonl
```

那是先前版本或不同 receiver 布局留下的产物。当前 `q3dc_receiver.py` 代码路径以 `frames/` 为主。

## Metadata 接收

`metadata_loop()`：

1. UDP bind 到 `metadataPort`。
2. `recvfrom(65535)`。
3. payload 按 UTF-8 解码。
4. `ensure_session("metadata", remote)`。
5. 解析 JSON。
6. `compact_frame_state(...)` 把完整 packet 压缩成面向导出的 frame state。
7. 写：
   - `frames/current_frame_state.json`
   - `frames/frame_state.jsonl`
8. 更新 `global_status.json`：
   - metadata count
   - metadata fps
   - latest remote

### 默认导出字段

`ExportOptions` 默认：

```text
coordinate_space = world
include_headset_pose = false
include_controller_buttons = false
include_camera_matrices = false
include_images = true
write_frame_history = true
```

因此默认 compact frame state 包含：

- frame id
- timestamp
- camera frame id
- camera timestamp
- camera imagePath
- camera world pose
- camera resolution/focalLength/principalPoint/sensorResolution/lensOffset
- controller world pose

默认不包含：

- headset pose
- controller buttons
- camera matrices

这些可以通过 CLI 参数或 GUI 选项打开。

## Video/debug 接收

`video_loop()`：

1. UDP bind 到 `videoPort`。
2. `recvfrom(65535)`。
3. `parse_video_payload(payload)`：
   - 若没有 `Q3DCBIN1`，按 legacy payload。
   - 若有 magic，读取 4-byte header length。
   - 解析 JSON header。
   - 剩余 bytes 是 video payload。
4. 若 `contentType == image/jpeg`，extension 为 `.jpg`。
5. 若 `include_images = true`，写入：

```text
frames/images/frame_<sourceCameraFrameId>.jpg
```

6. 更新 `global_status.json`：
   - image count
   - image fps
   - latest remote

## GUI 行为

`q3dc_receiver_gui.py`：

- 启动前检查 UDP 端口是否可用。
- 创建 `Receiver(... lazy_session=True ...)`。
- 用后台 thread 跑 receiver。
- UI 每 100ms drain events。
- `handle_metadata()` 更新 metadata 文本与 summary。
- `handle_video()` 更新 latest frame id 和 JPEG preview。
- `update_preview()` 只预览 `.jpg`；若收到 H.264/H.265 `.bin`，会显示不是 JPEG frame。

这意味着正式视频改成 H.264/H.265 后，GUI 需要新预览逻辑，不能继续依赖 Pillow 打开 JPEG。

## 当前接收器限制

1. UDP 单包最大读取 `65535`，与 Quest 端 `maxPacketBytes = 60000` 配合；没有分片重组。
2. 当前 video 写盘按“一个 UDP packet = 一张图片或一个 binary packet”处理。
3. H.264/H.265 会被保存成 `.bin`，但不会 mux 成 mp4，也不会解码预览。
4. 没有丢包检测：
   - 可以从 sequence id 推断，但当前未做 gap report。
5. 没有按 frame id 合并 metadata/video 的接收侧索引。
6. 当前 compact metadata 默认不写 headset pose/buttons/matrices，可能让用户误以为没有采集；实际完整 payload 中存在，导出选项默认关闭。

## 与 Quest 端字段的对应关系

### Metadata

Quest:

```text
MergedMetadataPacket
  header.frameId
  header.timestampUnixMs
  header.sequenceId
  snapshot
```

Windows:

```text
compact_frame_state(...)
  frameId
  timestampUnixMs
  camera
  controllers
  optional headset/buttons/matrices
```

### Debug video

Quest:

```text
EncodedVideoPacketHeader
  header.frameId
  sourceCameraFrameId
  encodedFrameId
  codec
  width
  height
  payloadByteLength
  contentType
```

Windows:

```text
video_filename(...)
  frame_<sourceCameraFrameId>.jpg
```

## 建议

短期：

- 在 `global_status.json` 中加入 sequence gap/drop 统计。
- 将 video header 也写入 `frames/video_headers.jsonl`，方便诊断。
- 明确新旧目录布局，避免 `video/` 与 `frames/images/` 混用。

正式视频：

- 若走 H.264/H.265：
  - 增加分片重组。
  - 保存 codec config/SPS/PPS/VPS。
  - 支持按 session mux 成 mp4 或 mkv。
  - GUI 预览改用 ffmpeg/OpenCV/播放器管线。
- 若走 WebRTC：
  - receiver 端不再是简单 UDP 文件接收器，需要 WebRTC peer/signaling。

