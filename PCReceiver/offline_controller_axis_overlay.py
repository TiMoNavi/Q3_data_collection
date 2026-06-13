#!/usr/bin/env python3
"""Draw controller XYZ axes on saved Quest passthrough debug frames."""

from __future__ import annotations

import argparse
import json
import math
import time
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
from typing import Any

try:
    from PIL import Image, ImageDraw, ImageFont
except ImportError as exc:  # pragma: no cover - exercised only on missing dependency
    raise SystemExit(
        "Pillow is required. Install it in this Python environment with: python -m pip install Pillow"
    ) from exc


AXES = (
    ("x", (1.0, 0.0, 0.0), (255, 48, 48)),
    ("y", (0.0, 1.0, 0.0), (48, 220, 80)),
    ("z", (0.0, 0.0, 1.0), (64, 128, 255)),
)
EPSILON = 1e-6


@dataclass
class ProjectionResult:
    pixel: tuple[float, float]
    viewport: tuple[float, float]
    local_z: float


@dataclass
class VideoFrameRecord:
    image_path: Path
    source_camera_frame_id: int
    timestamp_unix_ms: int
    sequence_id: int
    encoded_frame_id: int | None


@dataclass
class OverlayStats:
    session_dir: Path
    metadata_records: int = 0
    video_records: int = 0
    matched_frames: int = 0
    timestamp_warnings: int = 0
    images_written: int = 0
    hands_drawn: int = 0
    axes_drawn: int = 0
    skipped: Counter[str] | None = None

    def __post_init__(self) -> None:
        if self.skipped is None:
            self.skipped = Counter()

    def print_summary(self) -> None:
        print(f"session={self.session_dir}")
        print(f"metadata_records={self.metadata_records}")
        print(f"video_records={self.video_records}")
        print(f"matched_frames={self.matched_frames}")
        print(f"timestamp_warnings={self.timestamp_warnings}")
        print(f"images_written={self.images_written}")
        print(f"hands_drawn={self.hands_drawn}")
        print(f"axes_drawn={self.axes_drawn}")
        if self.skipped:
            print("skipped:")
            for reason, count in sorted(self.skipped.items()):
                print(f"  {reason}={count}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Offline overlay tool for PCReceiver captures. It matches saved DEBUG_JPEG/PNG "
            "frames to merged metadata by sourceCameraFrameId and draws left/right controller "
            "world-space XYZ axes using the Passthrough Camera API camera pose and intrinsics."
        )
    )
    parser.add_argument(
        "session",
        nargs="?",
        type=Path,
        help="Session directory containing metadata/ and video/. If omitted, use latest under --capture-root.",
    )
    parser.add_argument(
        "--capture-root",
        type=Path,
        default=Path("captures"),
        help="Root searched when session is omitted. Default: captures",
    )
    parser.add_argument("--output-dir", type=Path, help="Output directory. Default: <session>/video/frames_overlay")
    parser.add_argument("--axis-length", type=float, default=0.12, help="Controller axis length in meters.")
    parser.add_argument("--line-width", type=int, default=3, help="Axis line width in pixels.")
    parser.add_argument("--timestamp-warning-ms", type=int, default=5, help="Warn when video/metadata timestamps differ.")
    parser.add_argument("--max-frames", type=int, default=0, help="Process at most this many video frames. 0 means all.")
    parser.add_argument(
        "--export-json",
        type=Path,
        help="Write a controller-axis-overlay/v1 JSON export beside the overlay images.",
    )
    parser.add_argument("--dry-run", action="store_true", help="Parse and project without writing overlay images.")
    parser.add_argument(
        "--write-empty",
        action="store_true",
        help="Write matched images even when no controller axis was drawn.",
    )
    parser.add_argument("--verbose", action="store_true", help="Print per-frame skip details.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    session_dir = resolve_session_dir(args.session, args.capture_root)
    output_dir = args.output_dir or (session_dir / "video" / "frames_overlay")

    metadata_by_frame = load_metadata_index(session_dir)
    video_records = load_video_records(session_dir)
    if args.max_frames > 0:
        video_records = video_records[: args.max_frames]

    stats = OverlayStats(
        session_dir=session_dir,
        metadata_records=len(metadata_by_frame),
        video_records=len(video_records),
    )

    if not args.dry_run:
        output_dir.mkdir(parents=True, exist_ok=True)

    export_frames: list[dict[str, Any]] = []
    for video_record in video_records:
        process_video_record(
            video_record=video_record,
            metadata_by_frame=metadata_by_frame,
            session_dir=session_dir,
            output_dir=output_dir,
            axis_length=args.axis_length,
            line_width=max(1, args.line_width),
            timestamp_warning_ms=max(0, args.timestamp_warning_ms),
            dry_run=args.dry_run,
            write_empty=args.write_empty,
            verbose=args.verbose,
            stats=stats,
            export_frames=export_frames if args.export_json is not None else None,
        )

    if args.export_json is not None:
        write_export_json(
            export_path=args.export_json,
            session_dir=session_dir,
            axis_length=args.axis_length,
            frames=export_frames,
        )

    stats.print_summary()
    if args.dry_run:
        print("dry_run=true")
    else:
        print(f"output_dir={output_dir}")
    if args.export_json is not None:
        print(f"export_json={args.export_json}")
    return 0


def resolve_session_dir(session_arg: Path | None, capture_root: Path) -> Path:
    if session_arg is not None:
        session_dir = session_arg.resolve()
    else:
        session_dir = find_latest_session(capture_root.resolve())

    required = (
        session_dir / "metadata" / "merged_metadata.jsonl",
        session_dir / "video" / "video_headers.jsonl",
    )
    missing = [str(path) for path in required if not path.exists()]
    if missing:
        raise SystemExit("Session is missing required files:\n" + "\n".join(missing))

    return session_dir


def find_latest_session(capture_root: Path) -> Path:
    candidates: list[Path] = []
    if not capture_root.exists():
        raise SystemExit(f"Capture root does not exist: {capture_root}")

    for metadata_path in capture_root.rglob("metadata/merged_metadata.jsonl"):
        session_dir = metadata_path.parent.parent
        if (session_dir / "video" / "video_headers.jsonl").exists():
            candidates.append(session_dir)

    if not candidates:
        raise SystemExit(f"No complete capture sessions found under: {capture_root}")

    return max(candidates, key=lambda path: (path / "metadata" / "merged_metadata.jsonl").stat().st_mtime)


def load_metadata_index(session_dir: Path) -> dict[int, dict[str, Any]]:
    path = session_dir / "metadata" / "merged_metadata.jsonl"
    records: dict[int, dict[str, Any]] = {}

    for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if not line.strip():
            continue

        try:
            outer = json.loads(line)
            payload_text = outer.get("payload")
            payload = json.loads(payload_text) if isinstance(payload_text, str) else payload_text
            snapshot = payload["snapshot"]
        except (KeyError, TypeError, json.JSONDecodeError) as exc:
            print(f"warning: skipping bad metadata line {line_number}: {exc}")
            continue

        for frame_id in extract_snapshot_frame_ids(snapshot):
            records[frame_id] = snapshot

    return records


def extract_snapshot_frame_ids(snapshot: dict[str, Any]) -> set[int]:
    frame_ids: set[int] = set()
    for path in (("frameId",), ("camera", "frameId")):
        value = get_nested(snapshot, path)
        if isinstance(value, (int, float)):
            frame_ids.add(int(value))
    return frame_ids


def load_video_records(session_dir: Path) -> list[VideoFrameRecord]:
    path = session_dir / "video" / "video_headers.jsonl"
    records: list[VideoFrameRecord] = []

    for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if not line.strip():
            continue

        try:
            record = json.loads(line)
            header = record["header"]
            source_frame_id = int(header.get("sourceCameraFrameId", header["header"]["frameId"]))
            timestamp_unix_ms = int(header["header"]["timestampUnixMs"])
            sequence_id = int(header["header"].get("sequenceId", len(records)))
            encoded_frame_id = header.get("encodedFrameId")
            if encoded_frame_id is not None:
                encoded_frame_id = int(encoded_frame_id)
            image_path = resolve_video_image_path(session_dir, record)
        except (KeyError, TypeError, ValueError, json.JSONDecodeError) as exc:
            print(f"warning: skipping bad video header line {line_number}: {exc}")
            continue

        if image_path.suffix.lower() not in {".jpg", ".jpeg", ".png"}:
            continue

        records.append(
            VideoFrameRecord(
                image_path=image_path,
                source_camera_frame_id=source_frame_id,
                timestamp_unix_ms=timestamp_unix_ms,
                sequence_id=sequence_id,
                encoded_frame_id=encoded_frame_id,
            )
        )

    return records


def resolve_video_image_path(session_dir: Path, record: dict[str, Any]) -> Path:
    absolute_path = record.get("absolutePath")
    if isinstance(absolute_path, str) and absolute_path:
        return Path(absolute_path)

    relative_path = record.get("file")
    if isinstance(relative_path, str) and relative_path:
        return session_dir / relative_path

    raise KeyError("absolutePath/file")


def process_video_record(
    video_record: VideoFrameRecord,
    metadata_by_frame: dict[int, dict[str, Any]],
    session_dir: Path,
    output_dir: Path,
    axis_length: float,
    line_width: int,
    timestamp_warning_ms: int,
    dry_run: bool,
    write_empty: bool,
    verbose: bool,
    stats: OverlayStats,
    export_frames: list[dict[str, Any]] | None,
) -> None:
    snapshot = metadata_by_frame.get(video_record.source_camera_frame_id)
    if snapshot is None:
        stats.skipped["metadata_missing"] += 1
        if verbose:
            print(f"skip frame={video_record.source_camera_frame_id}: metadata_missing")
        return

    stats.matched_frames += 1

    metadata_timestamp = int(get_nested(snapshot, ("camera", "timestampUnixMs"), default=0) or 0)
    timestamp_delta = abs(video_record.timestamp_unix_ms - metadata_timestamp)
    if timestamp_delta > timestamp_warning_ms:
        stats.timestamp_warnings += 1
        if verbose:
            print(
                "warning: timestamp_delta_ms="
                f"{timestamp_delta} frame={video_record.source_camera_frame_id}"
            )

    if not video_record.image_path.exists():
        stats.skipped["image_missing"] += 1
        if verbose:
            print(f"skip frame={video_record.source_camera_frame_id}: image_missing {video_record.image_path}")
        return

    try:
        image = Image.open(video_record.image_path).convert("RGB")
    except OSError as exc:
        stats.skipped["image_open_failed"] += 1
        if verbose:
            print(f"skip frame={video_record.source_camera_frame_id}: image_open_failed {exc}")
        return

    draw = ImageDraw.Draw(image)
    font = ImageFont.load_default()
    axes_before = stats.axes_drawn

    for side in ("left", "right"):
        draw_controller_axes(
            draw=draw,
            font=font,
            image_size=image.size,
            snapshot=snapshot,
            side=side,
            axis_length=axis_length,
            line_width=line_width,
            verbose=verbose,
            stats=stats,
        )

    axes_for_frame = stats.axes_drawn - axes_before
    should_write_image = (not dry_run) and (axes_for_frame > 0 or write_empty)
    output_path = output_dir / video_record.image_path.name

    if export_frames is not None:
        export_frames.append(
            build_export_frame(
                video_record=video_record,
                snapshot=snapshot,
                session_dir=session_dir,
                image_size=image.size,
                overlay_path=output_path if should_write_image else None,
                axis_length=axis_length,
            )
        )

    if dry_run:
        return

    if not should_write_image:
        return

    image.save(output_path)
    stats.images_written += 1


def write_export_json(
    export_path: Path,
    session_dir: Path,
    axis_length: float,
    frames: list[dict[str, Any]],
) -> None:
    export_path.parent.mkdir(parents=True, exist_ok=True)
    document = {
        "schemaVersion": "controller-axis-overlay/v1",
        "coordinateSystem": "unity-world-meters",
        "cameraModel": "quest-pca-pinhole",
        "axisLengthMeters": axis_length,
        "source": {
            "sessionDir": normalize_path(session_dir),
            "metadataPath": "metadata/merged_metadata.jsonl",
            "videoHeadersPath": "video/video_headers.jsonl",
            "generatedAtUnixMs": int(time.time() * 1000),
            "toolName": "PCReceiver/offline_controller_axis_overlay.py",
        },
        "frames": frames,
    }
    export_path.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def build_export_frame(
    video_record: VideoFrameRecord,
    snapshot: dict[str, Any],
    session_dir: Path,
    image_size: tuple[int, int],
    overlay_path: Path | None,
    axis_length: float,
) -> dict[str, Any]:
    camera = snapshot.get("camera") if isinstance(snapshot.get("camera"), dict) else {}
    frame: dict[str, Any] = {
        "sourceCameraFrameId": video_record.source_camera_frame_id,
        "sequenceId": video_record.sequence_id,
        "timestampUnixMs": video_record.timestamp_unix_ms,
        "imagePath": relative_or_normalized(video_record.image_path, session_dir),
        "imageSize": {"width": image_size[0], "height": image_size[1]},
        "camera": build_export_camera(camera),
        "controllers": {
            "left": build_export_controller(snapshot, "left", image_size, axis_length),
            "right": build_export_controller(snapshot, "right", image_size, axis_length),
        },
    }
    if video_record.encoded_frame_id is not None:
        frame["encodedFrameId"] = video_record.encoded_frame_id
    if overlay_path is not None:
        frame["overlayImagePath"] = relative_or_normalized(overlay_path, session_dir)
    return frame


def build_export_camera(camera: dict[str, Any]) -> dict[str, Any]:
    return {
        "frameId": int(camera.get("frameId", 0) or 0),
        "timestampUnixMs": int(camera.get("timestampUnixMs", 0) or 0),
        "positionWorld": vec3_to_dict(read_vec3(camera.get("cameraPosition"))),
        "rotationWorld": quat_to_dict(read_quat(camera.get("cameraRotation"))),
        "resolution": size_to_dict(read_vec2(camera.get("resolution"))),
        "sensorResolution": size_to_dict(read_vec2(camera.get("sensorResolution"))),
        "focalLength": vec2_to_dict(read_vec2(camera.get("focalLength"))),
        "principalPoint": vec2_to_dict(read_vec2(camera.get("principalPoint"))),
        "cameraLocalToWorldMatrix": matrix_to_rows(read_matrix4(camera.get("cameraLocalToWorldMatrix"))),
        "cameraWorldToLocalMatrix": matrix_to_rows(read_matrix4(camera.get("cameraWorldToLocalMatrix"))),
    }


def build_export_controller(
    snapshot: dict[str, Any],
    side: str,
    image_size: tuple[int, int],
    axis_length: float,
) -> dict[str, Any]:
    controller = snapshot.get("controller")
    camera = snapshot.get("camera")
    if not isinstance(controller, dict) or not isinstance(camera, dict):
        return {"hand": side, "isValid": False, "skipReason": "record_missing"}

    position = read_vec3(controller.get(f"world{side.capitalize()}Position"))
    rotation = read_quat(controller.get(f"world{side.capitalize()}Rotation"))
    timestamp_unix_ms = int(controller.get("timestampUnixMs", 0) or 0)
    if position is None or rotation is None:
        return {"hand": side, "isValid": False, "skipReason": "controller_world_pose_missing"}

    if is_zero_position_and_identity_rotation(position, rotation):
        return {"hand": side, "isValid": False, "skipReason": "controller_world_pose_invalid"}

    pivot = project_world_point(position, camera, image_size)
    if pivot is None:
        return {"hand": side, "isValid": False, "skipReason": "pivot_behind_camera"}

    axes: dict[str, Any] = {}
    for axis_name, local_axis, color in AXES:
        endpoint_world = vec_add(position, quat_rotate(rotation, vec_scale(local_axis, axis_length)))
        endpoint = project_world_point(endpoint_world, camera, image_size)
        if endpoint is None:
            axes[axis_name] = {
                "isVisible": False,
                "skipReason": "endpoint_behind_camera",
                "endpointWorld": vec3_to_dict(endpoint_world),
                "colorRgb": list(color),
            }
            continue

        axes[axis_name] = {
            "isVisible": True,
            "endpointWorld": vec3_to_dict(endpoint_world),
            "startPixel": vec2_to_dict(pivot.pixel),
            "endPixel": vec2_to_dict(endpoint.pixel),
            "colorRgb": list(color),
        }

    return {
        "hand": side,
        "isValid": True,
        "timestampUnixMs": timestamp_unix_ms,
        "positionWorld": vec3_to_dict(position),
        "rotationWorld": quat_to_dict(rotation),
        "pivotPixel": vec2_to_dict(pivot.pixel),
        "axes": axes,
    }


def draw_controller_axes(
    draw: ImageDraw.ImageDraw,
    font: ImageFont.ImageFont,
    image_size: tuple[int, int],
    snapshot: dict[str, Any],
    side: str,
    axis_length: float,
    line_width: int,
    verbose: bool,
    stats: OverlayStats,
) -> None:
    controller = snapshot.get("controller")
    camera = snapshot.get("camera")
    if not isinstance(controller, dict) or not isinstance(camera, dict):
        stats.skipped[f"{side}_record_missing"] += 1
        return

    position = read_vec3(controller.get(f"world{side.capitalize()}Position"))
    rotation = read_quat(controller.get(f"world{side.capitalize()}Rotation"))
    if position is None or rotation is None:
        stats.skipped[f"{side}_controller_world_pose_missing"] += 1
        return

    if is_zero_position_and_identity_rotation(position, rotation):
        stats.skipped[f"{side}_controller_world_pose_invalid"] += 1
        if verbose:
            print(f"skip {side}: controller_world_pose_invalid")
        return

    pivot = project_world_point(position, camera, image_size)
    if pivot is None:
        stats.skipped[f"{side}_pivot_behind_camera"] += 1
        if verbose:
            print(f"skip {side}: pivot_behind_camera")
        return

    axes_drawn = 0
    for axis_name, local_axis, color in AXES:
        endpoint_world = vec_add(position, quat_rotate(rotation, vec_scale(local_axis, axis_length)))
        endpoint = project_world_point(endpoint_world, camera, image_size)
        if endpoint is None:
            stats.skipped[f"{side}_{axis_name}_endpoint_behind_camera"] += 1
            continue

        if draw_arrow(draw, pivot.pixel, endpoint.pixel, color, line_width, image_size):
            stats.axes_drawn += 1
            axes_drawn += 1
        else:
            stats.skipped[f"{side}_{axis_name}_outside_image"] += 1

    if axes_drawn > 0:
        stats.hands_drawn += 1
        draw_pivot_label(draw, font, pivot.pixel, side[0].upper(), image_size)


def project_world_point(
    world_point: tuple[float, float, float],
    camera: dict[str, Any],
    image_size: tuple[int, int],
) -> ProjectionResult | None:
    world_to_local = read_matrix4(camera.get("cameraWorldToLocalMatrix"))
    focal_length = read_vec2(camera.get("focalLength"))
    principal_point = read_vec2(camera.get("principalPoint"))
    sensor_resolution = read_vec2(camera.get("sensorResolution"))
    current_resolution = read_vec2(camera.get("resolution"))
    if (
        world_to_local is None
        or focal_length is None
        or principal_point is None
        or sensor_resolution is None
        or current_resolution is None
    ):
        return None

    local = transform_point(world_to_local, world_point)
    if local[2] <= EPSILON:
        return None

    crop_x, crop_y, crop_width, crop_height = calculate_sensor_crop(sensor_resolution, current_resolution)
    sensor_u = (local[0] / local[2]) * focal_length[0] + principal_point[0]
    sensor_v = (local[1] / local[2]) * focal_length[1] + principal_point[1]
    viewport_x = (sensor_u - crop_x) / crop_width
    viewport_y = (sensor_v - crop_y) / crop_height

    image_width, image_height = image_size
    pixel_x = viewport_x * image_width
    pixel_y = (1.0 - viewport_y) * image_height
    return ProjectionResult(pixel=(pixel_x, pixel_y), viewport=(viewport_x, viewport_y), local_z=local[2])


def calculate_sensor_crop(
    sensor_resolution: tuple[float, float],
    current_resolution: tuple[float, float],
) -> tuple[float, float, float, float]:
    sensor_x = max(EPSILON, sensor_resolution[0])
    sensor_y = max(EPSILON, sensor_resolution[1])
    current_x = max(EPSILON, current_resolution[0])
    current_y = max(EPSILON, current_resolution[1])
    scale_x = current_x / sensor_x
    scale_y = current_y / sensor_y
    scale_max = max(scale_x, scale_y, EPSILON)
    scale_x /= scale_max
    scale_y /= scale_max
    crop_width = sensor_x * scale_x
    crop_height = sensor_y * scale_y
    crop_x = sensor_x * (1.0 - scale_x) * 0.5
    crop_y = sensor_y * (1.0 - scale_y) * 0.5
    return crop_x, crop_y, crop_width, crop_height


def draw_arrow(
    draw: ImageDraw.ImageDraw,
    start: tuple[float, float],
    end: tuple[float, float],
    color: tuple[int, int, int],
    line_width: int,
    image_size: tuple[int, int],
) -> bool:
    clipped = clip_line_to_rect(start, end, image_size[0], image_size[1])
    if clipped is None:
        return False

    clipped_start, clipped_end = clipped
    draw.line((clipped_start, clipped_end), fill=color, width=line_width)

    dx = clipped_end[0] - clipped_start[0]
    dy = clipped_end[1] - clipped_start[1]
    length = math.hypot(dx, dy)
    if length <= EPSILON:
        return True

    ux = dx / length
    uy = dy / length
    arrow_length = max(8.0, line_width * 3.5)
    arrow_width = max(5.0, line_width * 2.0)
    left = (
        clipped_end[0] - ux * arrow_length - uy * arrow_width,
        clipped_end[1] - uy * arrow_length + ux * arrow_width,
    )
    right = (
        clipped_end[0] - ux * arrow_length + uy * arrow_width,
        clipped_end[1] - uy * arrow_length - ux * arrow_width,
    )
    draw.polygon((clipped_end, left, right), fill=color)
    return True


def draw_pivot_label(
    draw: ImageDraw.ImageDraw,
    font: ImageFont.ImageFont,
    pivot: tuple[float, float],
    label: str,
    image_size: tuple[int, int],
) -> None:
    x = min(max(int(round(pivot[0])) + 5, 0), max(0, image_size[0] - 12))
    y = min(max(int(round(pivot[1])) + 5, 0), max(0, image_size[1] - 12))
    draw.ellipse((x - 5, y - 5, x + 5, y + 5), fill=(255, 255, 255), outline=(0, 0, 0), width=1)
    draw.text((x + 7, y - 7), label, fill=(0, 0, 0), font=font)
    draw.text((x + 6, y - 8), label, fill=(255, 255, 255), font=font)


def clip_line_to_rect(
    start: tuple[float, float],
    end: tuple[float, float],
    width: int,
    height: int,
) -> tuple[tuple[float, float], tuple[float, float]] | None:
    # Liang-Barsky line clipping.
    x0, y0 = start
    x1, y1 = end
    dx = x1 - x0
    dy = y1 - y0
    p = (-dx, dx, -dy, dy)
    q = (x0, (width - 1) - x0, y0, (height - 1) - y0)
    u1 = 0.0
    u2 = 1.0

    for pi, qi in zip(p, q):
        if abs(pi) <= EPSILON:
            if qi < 0:
                return None
            continue

        ratio = qi / pi
        if pi < 0:
            if ratio > u2:
                return None
            if ratio > u1:
                u1 = ratio
        else:
            if ratio < u1:
                return None
            if ratio < u2:
                u2 = ratio

    return (x0 + u1 * dx, y0 + u1 * dy), (x0 + u2 * dx, y0 + u2 * dy)


def read_vec2(value: Any) -> tuple[float, float] | None:
    if isinstance(value, dict):
        try:
            return float(value["x"]), float(value["y"])
        except (KeyError, TypeError, ValueError):
            return None
    return None


def read_vec3(value: Any) -> tuple[float, float, float] | None:
    if isinstance(value, dict):
        try:
            return float(value["x"]), float(value["y"]), float(value["z"])
        except (KeyError, TypeError, ValueError):
            return None
    return None


def read_quat(value: Any) -> tuple[float, float, float, float] | None:
    if isinstance(value, dict):
        try:
            return float(value["x"]), float(value["y"]), float(value["z"]), float(value["w"])
        except (KeyError, TypeError, ValueError):
            return None
    return None


def read_matrix4(value: Any) -> tuple[tuple[float, float, float, float], ...] | None:
    if not isinstance(value, dict):
        return None
    try:
        return tuple(
            tuple(float(value[f"e{row}{col}"]) for col in range(4))
            for row in range(4)
        )
    except (KeyError, TypeError, ValueError):
        return None


def get_nested(value: dict[str, Any], path: tuple[str, ...], default: Any = None) -> Any:
    current: Any = value
    for key in path:
        if not isinstance(current, dict) or key not in current:
            return default
        current = current[key]
    return current


def transform_point(
    matrix: tuple[tuple[float, float, float, float], ...],
    point: tuple[float, float, float],
) -> tuple[float, float, float]:
    x, y, z = point
    return (
        matrix[0][0] * x + matrix[0][1] * y + matrix[0][2] * z + matrix[0][3],
        matrix[1][0] * x + matrix[1][1] * y + matrix[1][2] * z + matrix[1][3],
        matrix[2][0] * x + matrix[2][1] * y + matrix[2][2] * z + matrix[2][3],
    )


def quat_rotate(
    quaternion: tuple[float, float, float, float],
    vector: tuple[float, float, float],
) -> tuple[float, float, float]:
    qx, qy, qz, qw = normalize_quaternion(quaternion)
    vx, vy, vz = vector

    # Optimized q * v * inverse(q).
    tx = 2.0 * (qy * vz - qz * vy)
    ty = 2.0 * (qz * vx - qx * vz)
    tz = 2.0 * (qx * vy - qy * vx)
    return (
        vx + qw * tx + (qy * tz - qz * ty),
        vy + qw * ty + (qz * tx - qx * tz),
        vz + qw * tz + (qx * ty - qy * tx),
    )


def normalize_quaternion(quaternion: tuple[float, float, float, float]) -> tuple[float, float, float, float]:
    x, y, z, w = quaternion
    length = math.sqrt(x * x + y * y + z * z + w * w)
    if length <= EPSILON:
        return 0.0, 0.0, 0.0, 1.0
    return x / length, y / length, z / length, w / length


def is_zero_position_and_identity_rotation(
    position: tuple[float, float, float],
    rotation: tuple[float, float, float, float],
) -> bool:
    x, y, z = position
    qx, qy, qz, qw = normalize_quaternion(rotation)
    return (
        abs(x) <= EPSILON
        and abs(y) <= EPSILON
        and abs(z) <= EPSILON
        and abs(qx) <= EPSILON
        and abs(qy) <= EPSILON
        and abs(qz) <= EPSILON
        and abs(abs(qw) - 1.0) <= EPSILON
    )


def vec_add(
    lhs: tuple[float, float, float],
    rhs: tuple[float, float, float],
) -> tuple[float, float, float]:
    return lhs[0] + rhs[0], lhs[1] + rhs[1], lhs[2] + rhs[2]


def vec_scale(vector: tuple[float, float, float], scale: float) -> tuple[float, float, float]:
    return vector[0] * scale, vector[1] * scale, vector[2] * scale


def relative_or_normalized(path: Path, base: Path) -> str:
    try:
        return normalize_path(path.resolve().relative_to(base.resolve()))
    except ValueError:
        return normalize_path(path)


def normalize_path(path: Path) -> str:
    return str(path).replace("\\", "/")


def vec2_to_dict(value: tuple[float, float] | None) -> dict[str, float]:
    if value is None:
        return {"x": 0.0, "y": 0.0}
    return {"x": value[0], "y": value[1]}


def vec3_to_dict(value: tuple[float, float, float] | None) -> dict[str, float]:
    if value is None:
        return {"x": 0.0, "y": 0.0, "z": 0.0}
    return {"x": value[0], "y": value[1], "z": value[2]}


def quat_to_dict(value: tuple[float, float, float, float] | None) -> dict[str, float]:
    if value is None:
        return {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0}
    return {"x": value[0], "y": value[1], "z": value[2], "w": value[3]}


def size_to_dict(value: tuple[float, float] | None) -> dict[str, int]:
    if value is None:
        return {"width": 1, "height": 1}
    return {"width": max(1, int(round(value[0]))), "height": max(1, int(round(value[1])))}


def matrix_to_rows(
    value: tuple[tuple[float, float, float, float], ...] | None,
) -> list[list[float]]:
    if value is None:
        return [
            [0.0, 0.0, 0.0, 0.0],
            [0.0, 0.0, 0.0, 0.0],
            [0.0, 0.0, 0.0, 0.0],
            [0.0, 0.0, 0.0, 0.0],
        ]
    return [list(row) for row in value]


if __name__ == "__main__":
    raise SystemExit(main())
