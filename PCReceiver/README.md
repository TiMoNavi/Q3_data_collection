# Q3 DataCapture PC Receiver

This folder contains the standalone PC-side receiver for the Quest DataCapture network layer.

## Protocol v0

- Discovery UDP port: `49000`
- Discovery request payload: `Q3DC_DISCOVER`
- Discovery response JSON:

```json
{
  "magic": "Q3DC",
  "protocolVersion": 1,
  "deviceRole": "pc_receiver",
  "metadataPort": 5001,
  "videoPort": 5000,
  "features": ["metadata", "debug_video"]
}
```

Runtime streams:

- Metadata UDP port: `5001`
- Media payload UDP port: `5000`

Unity should only send metadata for `MergedFrameSnapshotRecord.isSendable == true`.

Video/debug packets use a small binary envelope:

```text
Q3DCBIN1            8 bytes ASCII magic
headerJsonLength    4 bytes little-endian unsigned length
headerJson          UTF-8 JSON, includes frameId/timestamp/encodedFrameId/codec/contentType
payload             DEBUG_JPEG, DEBUG_MJPEG frame, H264, or H265 bytes
```

Full merged metadata is not exported directly by default. The receiver filters it into the formal capture schema described below.

Current encoding boundary:

- `DEBUG_JPEG` is a low-frequency debug image path used to validate discovery, UDP transport, packet capture, and timestamp pairing.
- `DEBUG_MJPEG` is a JPEG-frame-sequence fallback that travels through the video output path, but it is not H.264/H.265.
- H.264/H.265 is produced by the Unity Android MediaCodec surface adapter path on device. The first connected stage encodes a GPU/EGL test pattern; the passthrough-camera texture bridge is the next step.
- Meta Passthrough Camera API provides live texture and frame metadata; it does not provide a ready-made encoded video stream.

## Run

```powershell
python .\PCReceiver\q3dc_receiver.py --bind 0.0.0.0 --out .\captures
```

On a PC with multiple network adapters, force discovery replies to use the WiFi
address that the Quest can reach:

```powershell
python .\PCReceiver\q3dc_receiver.py --bind 0.0.0.0 --advertise-ip 192.168.3.92 --out .\captures
```

Formal export field options:

```powershell
python .\PCReceiver\q3dc_receiver.py `
  --bind 0.0.0.0 `
  --advertise-ip 172.20.10.2 `
  --task-name q3_room_take_001 `
  --coordinate-space world `
  --include-headset-pose `
  --include-camera-matrices
```

GUI receiver:

```powershell
python .\PCReceiver\q3dc_receiver_gui.py
```

In the GUI, choose the Quest-facing network interface, for example
`WLAN | 192.168.3.92/24`, then start the receiver. Start only opens the PC
listener and pairing/discovery log. A recording session folder is created later,
when the headset starts recording and the first metadata or video/debug packet
arrives.

The GUI shows discovery, metadata/media packet counts, the last headset endpoint,
the latest metadata JSON, and a JPEG preview for `DEBUG_JPEG` and `DEBUG_MJPEG` frame payloads. It also exposes formal
export field switches:

- Task name.
- Coordinate space: `world`, `calibrated`, or `both`.
- Include headset pose.
- Include controller buttons.
- Include camera matrices.
- Save media payloads.
- Write frame history.

Each headset recording creates a timestamped session folder under the selected
capture root:

```text
captures/
  _receiver/
    discovery/discovery_events.jsonl
    logs/receiver.log

captures/q3dc_session_YYYYMMDD_HHMMSS/
  session_manifest.json
  global_status.json
  discovery/discovery_events.jsonl
  frames/current_frame_state.json
  frames/frame_state.jsonl
  frames/images/frame_000031.jpg
  frames/video_mjpeg/frame_000031.jpg
  frames/video_packets/packet_000004.h264
  logs/receiver.log
```

The receiver writes:

- `global_status.json`: one session-level status file with task name, PC IP, ports, counters, frame rates, and selected export fields.
- `frames/current_frame_state.json`: overwritten every metadata frame with the latest filtered frame state.
- `frames/frame_state.jsonl`: optional concise per-frame history.
- `frames/images/frame_000031.jpg`: optional saved `DEBUG_JPEG` images named by source camera frame id.
- `frames/video_mjpeg/frame_000031.jpg`: optional saved `DEBUG_MJPEG` JPEG frame payloads named by source camera frame id.
- `frames/video_packets/*.h264` / `*.h265`: optional saved encoded access-unit payloads. Network fragmentation/reassembly is still planned, so large encoded frames may require the Phase 4.5 protocol before they are robust over UDP.
- `discovery/discovery_events.jsonl`
- `logs/receiver.log`

Default frame export is intentionally small:

- Camera world pose.
- Controller world pose.
- Camera metadata/intrinsics: resolution, focal length, principal point, sensor resolution, lens offset.

Optional fields add headset pose, calibrated coordinate-space poses, controller
buttons, and camera matrices.

`DEBUG_JPEG` and `DEBUG_MJPEG` validate transport and image/metadata alignment; they are not a production H.264/H.265 video stream. H.264/H.265 payloads are saved separately so they can be inspected with tools such as ffprobe/ffplay after the sender-side MediaCodec path is enabled.

`offline_controller_axis_overlay.py` currently reads historical raw sessions
with `metadata/merged_metadata.jsonl` and `video/video_headers.jsonl`. It needs
an adapter before it can consume the new formal export layout directly.

Windows Firewall may ask for permission the first time Python opens UDP ports.
