#!/usr/bin/env python3
import argparse
import json
import socket
import threading
import time
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Callable


MAGIC = "Q3DC"
DISCOVERY_REQUEST = b"Q3DC_DISCOVER"
VIDEO_ENVELOPE_MAGIC = b"Q3DCBIN1"


def now_ms() -> int:
    return int(time.time() * 1000)


def datetime_stamp() -> str:
    return time.strftime("%Y%m%d_%H%M%S")


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def parse_metadata_payload(text: str) -> dict[str, Any] | None:
    try:
        value = json.loads(text)
    except json.JSONDecodeError:
        return None

    return value if isinstance(value, dict) else None


def get_nested(value: dict[str, Any] | None, *keys: str, default: Any = None) -> Any:
    current: Any = value
    for key in keys:
        if not isinstance(current, dict):
            return default
        current = current.get(key)
    return current if current is not None else default


@dataclass(frozen=True)
class ExportOptions:
    task_name: str = "q3_data_collection"
    coordinate_space: str = "world"
    include_headset_pose: bool = False
    include_controller_buttons: bool = False
    include_camera_matrices: bool = False
    include_images: bool = True
    write_frame_history: bool = True


def pose(position: Any, rotation: Any) -> dict[str, Any]:
    return {
        "position": position,
        "rotation": rotation,
    }


def selected_controller_pose(controller: dict[str, Any], options: ExportOptions) -> dict[str, Any]:
    result: dict[str, Any] = {
        "timestampUnixMs": controller.get("timestampUnixMs"),
    }

    if options.coordinate_space in ("world", "both"):
        result["world"] = {
            "left": pose(controller.get("worldLeftPosition"), controller.get("worldLeftRotation")),
            "right": pose(controller.get("worldRightPosition"), controller.get("worldRightRotation")),
        }

    if options.coordinate_space in ("calibrated", "both"):
        result["calibrated"] = {
            "left": pose(controller.get("calibratedLeftPosition"), controller.get("calibratedLeftRotation")),
            "right": pose(controller.get("calibratedRightPosition"), controller.get("calibratedRightRotation")),
        }

    if options.include_controller_buttons:
        result["buttons"] = {
            "leftTriggerPressed": controller.get("leftTriggerPressed"),
            "leftGripPressed": controller.get("leftGripPressed"),
            "leftPrimaryButtonPressed": controller.get("leftPrimaryButtonPressed"),
            "leftSecondaryButtonPressed": controller.get("leftSecondaryButtonPressed"),
            "rightTriggerPressed": controller.get("rightTriggerPressed"),
            "rightGripPressed": controller.get("rightGripPressed"),
            "rightPrimaryButtonPressed": controller.get("rightPrimaryButtonPressed"),
            "rightSecondaryButtonPressed": controller.get("rightSecondaryButtonPressed"),
        }

    return result


def selected_headset_pose(headset: dict[str, Any], options: ExportOptions) -> dict[str, Any] | None:
    if not options.include_headset_pose:
        return None

    result: dict[str, Any] = {
        "timestampUnixMs": headset.get("timestampUnixMs"),
    }

    if options.coordinate_space in ("world", "both"):
        result["world"] = {
            "centerEye": headset.get("worldCenterEye"),
        }

    if options.coordinate_space in ("calibrated", "both"):
        result["calibrated"] = {
            "centerEye": headset.get("calibratedCenterEye"),
        }

    return result


def compact_frame_state(
    packet: dict[str, Any],
    received_at_unix_ms: int,
    remote: str,
    options: ExportOptions,
) -> dict[str, Any]:
    header = packet.get("header", {})
    snapshot = packet.get("snapshot", {})
    camera = snapshot.get("camera", {}) if isinstance(snapshot, dict) else {}
    camera_image = snapshot.get("cameraImage", camera) if isinstance(snapshot, dict) else {}
    camera_timing = snapshot.get("cameraTiming", camera) if isinstance(snapshot, dict) else {}
    camera_pose = snapshot.get("cameraPose", camera) if isinstance(snapshot, dict) else {}
    camera_metadata = snapshot.get("cameraMetadata", camera) if isinstance(snapshot, dict) else {}
    camera_stream_state = snapshot.get("cameraStreamState", {}) if isinstance(snapshot, dict) else {}
    controller = snapshot.get("controller", {}) if isinstance(snapshot, dict) else {}
    headset = snapshot.get("headset", {}) if isinstance(snapshot, dict) else {}

    frame_id = header.get("frameId", snapshot.get("frameId"))
    timestamp_unix_ms = header.get("timestampUnixMs", snapshot.get("timestampUnixMs"))
    camera_frame_id = camera_image.get("frameId", camera_timing.get("frameId", frame_id))
    image_path = None
    if options.include_images and isinstance(camera_frame_id, int):
        image_path = f"frames/images/frame_{camera_frame_id:06d}.jpg"

    camera_parameters = {
        "resolution": camera_metadata.get("currentResolution", camera_image.get("resolution")),
        "focalLength": camera_metadata.get("focalLength"),
        "principalPoint": camera_metadata.get("principalPoint"),
        "sensorResolution": camera_metadata.get("sensorResolution"),
        "lensOffset": camera_metadata.get("lensOffset"),
        "hasDistortionData": camera_metadata.get("hasDistortionData"),
        "distortionCoefficients": camera_metadata.get("distortionCoefficients"),
        "cameraEye": camera_stream_state.get("cameraEye"),
        "requestedResolution": camera_stream_state.get("requestedResolution"),
        "requestedMaxFramerate": camera_stream_state.get("requestedMaxFramerate"),
        "measuredFramerate": camera_stream_state.get("measuredFramerate"),
    }
    if options.include_camera_matrices:
        camera_parameters["projectionMatrix"] = camera_metadata.get("projectionMatrix")
        camera_parameters["cameraLocalToWorldMatrix"] = camera_metadata.get("cameraLocalToWorldMatrix")
        camera_parameters["cameraWorldToLocalMatrix"] = camera_metadata.get("cameraWorldToLocalMatrix")

    result: dict[str, Any] = {
        "frameId": frame_id,
        "timestampUnixMs": timestamp_unix_ms,
        "receivedAtUnixMs": received_at_unix_ms,
        "remote": remote,
        "camera": {
            "frameId": camera_frame_id,
            "timestampUnixMs": camera_timing.get("timestampUnixMs", camera_image.get("timestampUnixMs")),
            "imagePath": image_path,
            "worldPose": pose(camera_pose.get("cameraPosition"), camera_pose.get("cameraRotation")),
            "parameters": camera_parameters,
        },
        "controllers": selected_controller_pose(controller, options),
    }

    headset_pose = selected_headset_pose(headset, options)
    if headset_pose is not None:
        result["headset"] = headset_pose

    return result


def video_filename(header: dict[str, Any] | None, video_index: int, extension: str) -> str:
    source_frame_id = get_nested(header, "sourceCameraFrameId")
    if isinstance(source_frame_id, int) and source_frame_id >= 0:
        return f"frame_{source_frame_id:06d}{extension}"

    packet_header_frame_id = get_nested(header, "header", "frameId")
    if isinstance(packet_header_frame_id, int) and packet_header_frame_id >= 0:
        return f"frame_{packet_header_frame_id:06d}{extension}"

    return f"frame_{video_index:06d}{extension}" if extension == ".jpg" else f"packet_{video_index:06d}{extension}"


def media_packet_info(header: dict[str, Any] | None) -> dict[str, str]:
    if not isinstance(header, dict):
        return {
            "kind": "legacy_debug_image",
            "folder": "images",
            "extension": ".jpg",
            "label": "Legacy debug image",
            "contentType": "",
            "codec": "",
        }

    content_type = str(header.get("contentType") or "").lower()
    codec = str(header.get("codec") or "").upper()

    if content_type == "image/jpeg" or codec == "DEBUG_JPEG":
        return {
            "kind": "debug_image",
            "folder": "images",
            "extension": ".jpg",
            "label": "Debug JPEG image",
            "contentType": content_type,
            "codec": codec,
        }

    if content_type == "video/x-motion-jpeg" or codec in ("DEBUG_MJPEG", "MJPEG"):
        return {
            "kind": "debug_mjpeg_frame",
            "folder": "video_mjpeg",
            "extension": ".jpg",
            "label": "Debug MJPEG video frame",
            "contentType": content_type,
            "codec": codec,
        }

    if content_type == "video/h264" or codec == "H264":
        return {
            "kind": "h264_video_packet",
            "folder": "video_packets",
            "extension": ".h264",
            "label": "H264 video packet",
            "contentType": content_type,
            "codec": codec,
        }

    if content_type == "video/h265" or codec in ("H265", "HEVC"):
        return {
            "kind": "h265_video_packet",
            "folder": "video_packets",
            "extension": ".h265",
            "label": "H265 video packet",
            "contentType": content_type,
            "codec": codec,
        }

    return {
        "kind": "unknown_media_packet",
        "folder": "video_packets",
        "extension": ".bin",
        "label": "Unknown media packet",
        "contentType": content_type,
        "codec": codec,
    }


class Receiver:
    def __init__(
        self,
        bind: str,
        advertise_ip: str | None,
        discovery_port: int,
        metadata_port: int,
        video_port: int,
        out_dir: Path,
        on_log: Callable[[str], None] | None = None,
        on_discovery: Callable[[dict[str, Any]], None] | None = None,
        on_metadata: Callable[[dict[str, Any]], None] | None = None,
        on_video: Callable[[dict[str, Any]], None] | None = None,
        on_session: Callable[[dict[str, Any]], None] | None = None,
        lazy_session: bool = False,
        export_options: ExportOptions | None = None,
    ):
        self.bind = bind
        self.advertise_ip = advertise_ip
        self.discovery_port = discovery_port
        self.metadata_port = metadata_port
        self.video_port = video_port
        self.out_dir = out_dir
        self.on_log = on_log
        self.on_discovery = on_discovery
        self.on_metadata = on_metadata
        self.on_video = on_video
        self.on_session = on_session
        self.lazy_session = lazy_session
        self.export_options = export_options or ExportOptions()
        self.session_dir: Path | None = None
        self.stop_event = threading.Event()
        self.video_index = 0
        self.lock = threading.Lock()
        self.open_files: list[Any] = []
        self.session_created_at_unix_ms = 0
        self.metadata_count = 0
        self.video_count = 0
        self.debug_image_count = 0
        self.debug_mjpeg_count = 0
        self.encoded_video_count = 0
        self.unknown_media_count = 0
        self.first_metadata_unix_ms = 0
        self.latest_metadata_unix_ms = 0
        self.first_video_unix_ms = 0
        self.latest_video_unix_ms = 0
        self.latest_metadata_remote = ""
        self.latest_video_remote = ""
        self.latest_trigger = ""
        self.latest_remote = ""
        self.latest_frame_state: dict[str, Any] | None = None

        self.out_dir.mkdir(parents=True, exist_ok=True)
        if self.lazy_session:
            self.discovery_dir = self.out_dir / "_receiver" / "discovery"
            self.log_dir = self.out_dir / "_receiver" / "logs"
            self.discovery_dir.mkdir(parents=True, exist_ok=True)
            self.log_dir.mkdir(parents=True, exist_ok=True)
            self.discovery_file = self._open_text(self.discovery_dir / "discovery_events.jsonl")
            self.log_file = self._open_text(self.log_dir / "receiver.log")
            self.frame_state_file = None
        else:
            self.open_session(self.out_dir, "startup", None)

    def _open_text(self, path: Path) -> Any:
        handle = path.open("a", encoding="utf-8")
        self.open_files.append(handle)
        return handle

    def open_session(self, session_dir: Path, trigger: str, remote: str | None) -> None:
        self.session_dir = session_dir
        self.out_dir = session_dir
        self.video_index = 0
        self.session_created_at_unix_ms = now_ms()
        self.metadata_count = 0
        self.video_count = 0
        self.debug_image_count = 0
        self.debug_mjpeg_count = 0
        self.encoded_video_count = 0
        self.unknown_media_count = 0
        self.first_metadata_unix_ms = 0
        self.latest_metadata_unix_ms = 0
        self.first_video_unix_ms = 0
        self.latest_video_unix_ms = 0
        self.latest_metadata_remote = ""
        self.latest_video_remote = ""
        self.latest_trigger = trigger
        self.latest_remote = remote or ""
        self.latest_frame_state = None

        self.discovery_dir = self.out_dir / "discovery"
        self.frame_dir = self.out_dir / "frames"
        self.image_dir = self.frame_dir / "images"
        self.video_mjpeg_dir = self.frame_dir / "video_mjpeg"
        self.video_packet_dir = self.frame_dir / "video_packets"
        self.log_dir = self.out_dir / "logs"

        directories = [self.discovery_dir, self.frame_dir, self.log_dir]
        if self.export_options.include_images:
            directories.extend([self.image_dir, self.video_mjpeg_dir, self.video_packet_dir])

        for directory in directories:
            directory.mkdir(parents=True, exist_ok=True)

        if not self.lazy_session:
            self.discovery_file = self._open_text(self.discovery_dir / "discovery_events.jsonl")
            self.log_file = self._open_text(self.log_dir / "receiver.log")

        self.frame_state_file = (
            self._open_text(self.frame_dir / "frame_state.jsonl")
            if self.export_options.write_frame_history
            else None
        )
        if self.lazy_session:
            self.log_file = self._open_text(self.log_dir / "receiver.log")

        self.write_manifest()
        self.write_global_status()
        self._emit(
            self.on_session,
            {
                "createdAtUnixMs": self.session_created_at_unix_ms,
                "sessionDir": str(self.session_dir),
                "trigger": trigger,
                "remote": remote,
                "taskName": self.export_options.task_name,
                "exportOptions": asdict(self.export_options),
            },
        )

    def ensure_session(self, trigger: str, remote: str | None) -> None:
        if self.session_dir is not None:
            return

        stamp = datetime_stamp()
        session_dir = self.out_dir / f"q3dc_session_{stamp}"
        self.open_session(session_dir, trigger, remote)
        self.log(f"Recording session folder created by {trigger}: {session_dir}")

    def write_manifest(self) -> None:
        manifest = {
            "createdAtUnixMs": self.session_created_at_unix_ms,
            "taskName": self.export_options.task_name,
            "sessionDir": str(self.out_dir),
            "trigger": self.latest_trigger,
            "remote": self.latest_remote,
            "bind": self.bind,
            "advertiseIp": self.advertise_ip,
            "ports": {
                "discovery": self.discovery_port,
                "metadata": self.metadata_port,
                "video": self.video_port,
            },
            "exportOptions": asdict(self.export_options),
            "layout": {
                "globalStatus": "global_status.json",
                "currentFrameState": "frames/current_frame_state.json",
                "frameStateHistory": "frames/frame_state.jsonl"
                if self.export_options.write_frame_history
                else None,
                "images": "frames/images/" if self.export_options.include_images else None,
                "debugMjpegFrames": "frames/video_mjpeg/" if self.export_options.include_images else None,
                "encodedVideoPackets": "frames/video_packets/" if self.export_options.include_images else None,
                "discovery": "discovery/discovery_events.jsonl",
                "logs": "logs/receiver.log",
                "receiverPairingLog": "../_receiver/logs/receiver.log" if self.lazy_session else None,
                "receiverPairingDiscovery": "../_receiver/discovery/discovery_events.jsonl" if self.lazy_session else None,
            },
        }
        write_json(self.out_dir / "session_manifest.json", manifest)

    def write_global_status(self) -> None:
        duration_metadata_seconds = max(0.0, (self.latest_metadata_unix_ms - self.first_metadata_unix_ms) / 1000)
        duration_video_seconds = max(0.0, (self.latest_video_unix_ms - self.first_video_unix_ms) / 1000)
        status = {
            "taskName": self.export_options.task_name,
            "createdAtUnixMs": self.session_created_at_unix_ms,
            "updatedAtUnixMs": now_ms(),
            "sessionDir": str(self.out_dir),
            "receiver": {
                "bind": self.bind,
                "advertiseIp": self.advertise_ip,
                "ports": {
                    "discovery": self.discovery_port,
                    "metadata": self.metadata_port,
                    "video": self.video_port,
                },
            },
            "exportOptions": asdict(self.export_options),
            "counters": {
                "metadataFrames": self.metadata_count,
                "mediaPackets": self.video_count,
                "imageFrames": self.debug_image_count,
                "debugMjpegVideoFrames": self.debug_mjpeg_count,
                "encodedVideoPackets": self.encoded_video_count,
                "unknownMediaPackets": self.unknown_media_count,
            },
            "rates": {
                "metadataFps": round(self.metadata_count / duration_metadata_seconds, 3)
                if duration_metadata_seconds > 0
                else 0,
                "mediaFps": round(self.video_count / duration_video_seconds, 3)
                if duration_video_seconds > 0
                else 0,
                "imageFps": round(self.debug_image_count / duration_video_seconds, 3)
                if duration_video_seconds > 0
                else 0,
            },
            "network": {
                "latestMetadataRemote": self.latest_metadata_remote,
                "latestMediaRemote": self.latest_video_remote,
                "latestImageRemote": self.latest_video_remote,
            },
        }
        write_json(self.out_dir / "global_status.json", status)

    def log(self, message: str) -> None:
        line = f"{time.strftime('%Y-%m-%dT%H:%M:%S')} {message}"
        print(line, flush=True)
        with self.lock:
            for log_file in self.open_files:
                if getattr(log_file, "name", "").endswith("receiver.log"):
                    log_file.write(line + "\n")
                    log_file.flush()
        self._emit(self.on_log, line)

    def _emit(self, callback: Callable[[Any], None] | None, value: Any) -> None:
        if callback is None:
            return

        try:
            callback(value)
        except Exception as exc:
            print(f"Receiver callback failed: {exc}", flush=True)

    def run(self) -> None:
        threads = [
            threading.Thread(target=self.discovery_loop, name="discovery", daemon=True),
            threading.Thread(target=self.metadata_loop, name="metadata", daemon=True),
            threading.Thread(target=self.video_loop, name="video", daemon=True),
        ]
        for thread in threads:
            thread.start()

        self.log(
            f"Listening on discovery={self.discovery_port}, "
            f"metadata={self.metadata_port}, video={self.video_port}, "
            f"bind={self.bind}, advertise_ip={self.advertise_ip or 'auto'}"
        )

        try:
            while not self.stop_event.is_set():
                time.sleep(0.2)
        except KeyboardInterrupt:
            self.log("Stopping.")
            self.stop_event.set()
        finally:
            self.close()

    def close(self) -> None:
        for handle in self.open_files:
            if not handle.closed:
                handle.close()
        self.open_files.clear()

    def discovery_loop(self) -> None:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock.bind((self.bind, self.discovery_port))
        sock.settimeout(0.5)
        response = json.dumps(
            {
                "magic": MAGIC,
                "protocolVersion": 1,
                "deviceRole": "pc_receiver",
                "receiverHost": self.advertise_ip or self.bind,
                "metadataPort": self.metadata_port,
                "videoPort": self.video_port,
                "features": ["metadata", "debug_video"],
            },
            separators=(",", ":"),
        ).encode("utf-8")

        while not self.stop_event.is_set():
            try:
                payload, address = sock.recvfrom(2048)
            except socket.timeout:
                continue

            if payload.strip() == DISCOVERY_REQUEST:
                sock.sendto(response, address)
                source = self.advertise_ip or self.bind
                discovery_record = {
                    "receivedAtUnixMs": now_ms(),
                    "remote": f"{address[0]}:{address[1]}",
                    "responseSource": source,
                }
                self._emit(self.on_discovery, discovery_record)
                with self.lock:
                    self.discovery_file.write(json.dumps(discovery_record, ensure_ascii=False) + "\n")
                    self.discovery_file.flush()
                self.log(f"Discovery response sent from {source} to {address[0]}:{address[1]}")

    def metadata_loop(self) -> None:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock.bind((self.bind, self.metadata_port))
        sock.settimeout(0.5)

        while not self.stop_event.is_set():
            try:
                payload, address = sock.recvfrom(65535)
            except socket.timeout:
                continue

            text = payload.decode("utf-8", errors="replace")
            remote = f"{address[0]}:{address[1]}"
            received_at = now_ms()
            self.ensure_session("metadata", remote)
            raw_record = {
                "receivedAtUnixMs": received_at,
                "remote": remote,
                "payload": text,
            }

            with self.lock:
                self.metadata_count += 1
                self.latest_metadata_unix_ms = received_at
                self.latest_metadata_remote = remote
                if self.first_metadata_unix_ms == 0:
                    self.first_metadata_unix_ms = received_at

                packet = parse_metadata_payload(text)
                if packet is not None:
                    frame_state = compact_frame_state(packet, received_at, remote, self.export_options)
                    self.latest_frame_state = frame_state
                    if self.frame_state_file is not None:
                        self.frame_state_file.write(
                            json.dumps(frame_state, ensure_ascii=False, separators=(",", ":")) + "\n"
                        )
                        self.frame_state_file.flush()
                    write_json(self.frame_dir / "current_frame_state.json", frame_state)

                self.write_global_status()

            self._emit(self.on_metadata, raw_record)
            self.log(f"Metadata {len(payload)} bytes from {address[0]}:{address[1]}")

    def video_loop(self) -> None:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock.bind((self.bind, self.video_port))
        sock.settimeout(0.5)

        while not self.stop_event.is_set():
            try:
                payload, address = sock.recvfrom(65535)
            except socket.timeout:
                continue

            header, video_payload = parse_video_payload(payload)
            remote = f"{address[0]}:{address[1]}"
            received_at = now_ms()
            self.ensure_session("video", remote)
            media_info = media_packet_info(header)
            extension = media_info["extension"]
            with self.lock:
                packet_name = video_filename(header, self.video_index, extension)
                self.video_index += 1
                path = None
                if self.export_options.include_images:
                    folder = {
                        "images": self.image_dir,
                        "video_mjpeg": self.video_mjpeg_dir,
                        "video_packets": self.video_packet_dir,
                    }.get(media_info["folder"], self.video_packet_dir)
                    path = folder / packet_name
                    path.write_bytes(video_payload)

                self.video_count += 1
                if media_info["kind"] in ("debug_image", "legacy_debug_image"):
                    self.debug_image_count += 1
                elif media_info["kind"] == "debug_mjpeg_frame":
                    self.debug_mjpeg_count += 1
                elif media_info["kind"] in ("h264_video_packet", "h265_video_packet"):
                    self.encoded_video_count += 1
                else:
                    self.unknown_media_count += 1
                self.latest_video_unix_ms = received_at
                self.latest_video_remote = remote
                if self.first_video_unix_ms == 0:
                    self.first_video_unix_ms = received_at

                image_record = {
                    "receivedAtUnixMs": received_at,
                    "remote": remote,
                    "file": str(path.relative_to(self.out_dir)) if path is not None else None,
                    "absolutePath": str(path) if path is not None else None,
                    "header": header,
                    "mediaKind": media_info["kind"],
                    "contentType": media_info["contentType"],
                    "codec": media_info["codec"],
                    "payloadByteLength": len(video_payload),
                }
                self.write_global_status()

            self._emit(self.on_video, image_record)
            if header is None:
                target = path.name if path is not None else "discarded"
                self.log(f"Legacy debug image packet {len(payload)} bytes from {address[0]}:{address[1]} -> {target}")
            else:
                frame_id = header.get("header", {}).get("frameId", "?")
                timestamp = header.get("header", {}).get("timestampUnixMs", "?")
                codec = header.get("codec", "?")
                target = path.name if path is not None else "discarded"
                self.log(
                    f"{media_info['label']} frame={frame_id} ts={timestamp} codec={codec} "
                    f"{len(video_payload)} bytes from {address[0]}:{address[1]} -> {target}"
                )


def parse_video_payload(payload: bytes) -> tuple[dict | None, bytes]:
    if len(payload) < 12 or not payload.startswith(VIDEO_ENVELOPE_MAGIC):
        return None, payload

    header_len = int.from_bytes(payload[8:12], byteorder="little", signed=False)
    header_start = 12
    header_end = header_start + header_len
    if header_len <= 0 or header_end > len(payload):
        return None, payload

    try:
        header = json.loads(payload[header_start:header_end].decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return None, payload

    return header, payload[header_end:]


def main() -> None:
    parser = argparse.ArgumentParser(description="Q3 DataCapture UDP receiver")
    parser.add_argument("--bind", default="0.0.0.0")
    parser.add_argument(
        "--advertise-ip",
        default=None,
        help=(
            "Force discovery responses to use this local source IP. "
            "Use the WiFi/LAN address that the Quest can reach, for example 192.168.3.92."
        ),
    )
    parser.add_argument("--discovery-port", type=int, default=49000)
    parser.add_argument("--metadata-port", type=int, default=5001)
    parser.add_argument("--video-port", type=int, default=5000)
    parser.add_argument("--out", default="captures")
    parser.add_argument("--task-name", default="q3_data_collection")
    parser.add_argument("--coordinate-space", choices=("world", "calibrated", "both"), default="world")
    parser.add_argument("--include-headset-pose", action="store_true")
    parser.add_argument("--include-controller-buttons", action="store_true")
    parser.add_argument("--include-camera-matrices", action="store_true")
    parser.add_argument("--no-images", action="store_true")
    parser.add_argument("--no-frame-history", action="store_true")
    parser.add_argument(
        "--startup-session",
        action="store_true",
        help="Create the session folder when the receiver starts. Default waits for headset metadata/image packets.",
    )
    args = parser.parse_args()

    options = ExportOptions(
        task_name=args.task_name,
        coordinate_space=args.coordinate_space,
        include_headset_pose=args.include_headset_pose,
        include_controller_buttons=args.include_controller_buttons,
        include_camera_matrices=args.include_camera_matrices,
        include_images=not args.no_images,
        write_frame_history=not args.no_frame_history,
    )

    Receiver(
        bind=args.bind,
        advertise_ip=args.advertise_ip,
        discovery_port=args.discovery_port,
        metadata_port=args.metadata_port,
        video_port=args.video_port,
        out_dir=Path(args.out),
        lazy_session=not args.startup_session,
        export_options=options,
    ).run()


if __name__ == "__main__":
    main()
