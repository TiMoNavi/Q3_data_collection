#!/usr/bin/env python3
import json
import os
import queue
import socket
import subprocess
import sys
import threading
import time
import tkinter as tk
from datetime import datetime
from pathlib import Path
from tkinter import filedialog, messagebox, ttk

from q3dc_receiver import ExportOptions, Receiver

try:
    from PIL import Image, ImageTk
except ImportError:
    Image = None
    ImageTk = None


DEFAULT_DISCOVERY_PORT = 49000
DEFAULT_METADATA_PORT = 5001
DEFAULT_VIDEO_PORT = 5000


def list_ipv4_interfaces() -> list[dict[str, str]]:
    command = (
        "Get-NetIPAddress -AddressFamily IPv4 | "
        "Where-Object { $_.IPAddress -notlike '127.*' -and $_.AddressState -eq 'Preferred' } | "
        "ForEach-Object { [PSCustomObject]@{"
        "InterfaceAlias=$_.InterfaceAlias;IPAddress=$_.IPAddress;PrefixLength=$_.PrefixLength"
        "} } | ConvertTo-Json -Compress"
    )
    try:
        completed = subprocess.run(
            ["powershell", "-NoProfile", "-Command", command],
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
        )
        text = completed.stdout.strip()
        if not text:
            return []
        data = json.loads(text)
        if isinstance(data, dict):
            data = [data]
        return [
            {
                "alias": str(item.get("InterfaceAlias", "")),
                "ip": str(item.get("IPAddress", "")),
                "prefix": str(item.get("PrefixLength", "")),
            }
            for item in data
            if item.get("IPAddress")
        ]
    except Exception:
        return fallback_interfaces()


def fallback_interfaces() -> list[dict[str, str]]:
    hostname = socket.gethostname()
    interfaces: list[dict[str, str]] = []
    seen: set[str] = set()
    try:
        for info in socket.getaddrinfo(hostname, None, socket.AF_INET):
            ip = info[4][0]
            if ip.startswith("127.") or ip in seen:
                continue
            seen.add(ip)
            interfaces.append({"alias": "IPv4", "ip": ip, "prefix": ""})
    except socket.gaierror:
        pass
    return interfaces


def find_existing_receiver_processes() -> list[str]:
    command = (
        "Get-CimInstance Win32_Process -Filter \"name = 'python.exe'\" | "
        "Where-Object { $_.CommandLine -like '*q3dc_receiver.py*' } | "
        "Select-Object ProcessId,CommandLine | ConvertTo-Json -Compress"
    )
    try:
        completed = subprocess.run(
            ["powershell", "-NoProfile", "-Command", command],
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
        )
        text = completed.stdout.strip()
        if not text:
            return []
        data = json.loads(text)
        if isinstance(data, dict):
            data = [data]
        current_pid = os.getpid()
        return [
            f"PID {item.get('ProcessId')}: {item.get('CommandLine')}"
            for item in data
            if int(item.get("ProcessId", -1)) != current_pid
        ]
    except Exception:
        return []


def check_udp_port_available(bind_ip: str, port: int) -> tuple[bool, str]:
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sock.bind((bind_ip, port))
        return True, ""
    except OSError as exc:
        return False, str(exc)
    finally:
        sock.close()


def parse_combo_ip(value: str) -> str | None:
    if not value or value.startswith("auto"):
        return None
    if value.startswith("0.0.0.0"):
        return "0.0.0.0"
    if "|" in value:
        value = value.split("|", 1)[1].strip()
    if "/" in value:
        value = value.split("/", 1)[0].strip()
    return value.strip() or None


class ReceiverWindow(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title("Q3 DataCapture Receiver")
        self.geometry("1180x820")
        self.minsize(980, 640)

        self.events: queue.Queue[tuple[str, object]] = queue.Queue()
        self.receiver: Receiver | None = None
        self.receiver_thread: threading.Thread | None = None
        self.output_dir = tk.StringVar(value=self.default_output_dir())
        self.current_session_dir: Path | None = None
        self.session_label = tk.StringVar(value="Waiting for headset recording")
        self.discovery_count = 0
        self.metadata_count = 0
        self.video_count = 0
        self.preview_image: object | None = None
        self.preview_status = tk.StringVar(value="No media frames yet")
        self.last_discovery_remote = tk.StringVar(value="-")
        self.last_metadata_remote = tk.StringVar(value="-")
        self.last_video_remote = tk.StringVar(value="-")
        self.last_frame = tk.StringVar(value="-")
        self.status = tk.StringVar(value="Stopped")

        self.discovery_port = tk.StringVar(value=str(DEFAULT_DISCOVERY_PORT))
        self.metadata_port = tk.StringVar(value=str(DEFAULT_METADATA_PORT))
        self.video_port = tk.StringVar(value=str(DEFAULT_VIDEO_PORT))
        self.bind_value = tk.StringVar(value="0.0.0.0 (all interfaces)")
        self.advertise_value = tk.StringVar(value="auto")
        self.task_name = tk.StringVar(value="q3_data_collection")
        self.coordinate_space = tk.StringVar(value="world")
        self.include_headset_pose = tk.BooleanVar(value=False)
        self.include_controller_buttons = tk.BooleanVar(value=False)
        self.include_camera_matrices = tk.BooleanVar(value=False)
        self.include_images = tk.BooleanVar(value=True)
        self.write_frame_history = tk.BooleanVar(value=True)

        self.interface_values: list[str] = []
        self._build_ui()
        self.refresh_interfaces()
        self.refresh_process_warning()
        self.after(100, self.drain_events)

    def default_output_dir(self) -> str:
        return str(Path.cwd() / "captures")

    def _build_ui(self) -> None:
        self.columnconfigure(0, weight=1)
        self.rowconfigure(2, weight=1)

        top = ttk.Frame(self, padding=12)
        top.grid(row=0, column=0, sticky="ew")
        top.columnconfigure(1, weight=1)
        top.columnconfigure(3, weight=1)

        ttk.Label(top, text="Quest network").grid(row=0, column=0, sticky="w")
        self.advertise_combo = ttk.Combobox(top, textvariable=self.advertise_value, state="readonly")
        self.advertise_combo.grid(row=0, column=1, sticky="ew", padx=(8, 16))

        ttk.Label(top, text="Bind").grid(row=0, column=2, sticky="w")
        self.bind_combo = ttk.Combobox(top, textvariable=self.bind_value, state="readonly")
        self.bind_combo.grid(row=0, column=3, sticky="ew", padx=(8, 16))

        ttk.Button(top, text="Refresh", command=self.refresh_interfaces).grid(row=0, column=4, sticky="ew")

        ttk.Label(top, text="Discovery").grid(row=1, column=0, sticky="w", pady=(8, 0))
        ttk.Entry(top, textvariable=self.discovery_port, width=8).grid(row=1, column=1, sticky="w", padx=(8, 16), pady=(8, 0))

        ttk.Label(top, text="Metadata").grid(row=1, column=2, sticky="w", pady=(8, 0))
        ttk.Entry(top, textvariable=self.metadata_port, width=8).grid(row=1, column=3, sticky="w", padx=(8, 16), pady=(8, 0))

        ttk.Label(top, text="Video").grid(row=1, column=4, sticky="w", pady=(8, 0))
        ttk.Entry(top, textvariable=self.video_port, width=8).grid(row=1, column=5, sticky="w", padx=(8, 0), pady=(8, 0))

        ttk.Label(top, text="Capture root").grid(row=2, column=0, sticky="w", pady=(8, 0))
        ttk.Entry(top, textvariable=self.output_dir).grid(row=2, column=1, columnspan=3, sticky="ew", padx=(8, 16), pady=(8, 0))
        ttk.Button(top, text="Browse", command=self.choose_output_dir).grid(row=2, column=4, sticky="ew", pady=(8, 0))
        ttk.Button(top, text="Open", command=self.open_output_dir).grid(row=2, column=5, sticky="ew", padx=(8, 0), pady=(8, 0))

        ttk.Label(top, text="Task").grid(row=3, column=0, sticky="w", pady=(8, 0))
        ttk.Entry(top, textvariable=self.task_name).grid(row=3, column=1, sticky="ew", padx=(8, 16), pady=(8, 0))
        ttk.Label(top, text="Coordinates").grid(row=3, column=2, sticky="w", pady=(8, 0))
        ttk.Combobox(
            top,
            textvariable=self.coordinate_space,
            values=("world", "calibrated", "both"),
            state="readonly",
            width=12,
        ).grid(row=3, column=3, sticky="w", padx=(8, 16), pady=(8, 0))

        export_options = ttk.LabelFrame(top, text="Frame export fields")
        export_options.grid(row=4, column=0, columnspan=6, sticky="ew", pady=(8, 0))
        ttk.Checkbutton(export_options, text="Headset pose", variable=self.include_headset_pose).grid(row=0, column=0, sticky="w", padx=8, pady=4)
        ttk.Checkbutton(export_options, text="Controller buttons", variable=self.include_controller_buttons).grid(row=0, column=1, sticky="w", padx=8, pady=4)
        ttk.Checkbutton(export_options, text="Camera matrices", variable=self.include_camera_matrices).grid(row=0, column=2, sticky="w", padx=8, pady=4)
        ttk.Checkbutton(export_options, text="Save media", variable=self.include_images).grid(row=0, column=3, sticky="w", padx=8, pady=4)
        ttk.Checkbutton(export_options, text="Frame history", variable=self.write_frame_history).grid(row=0, column=4, sticky="w", padx=8, pady=4)

        actions = ttk.Frame(self, padding=(12, 0, 12, 8))
        actions.grid(row=1, column=0, sticky="ew")
        actions.columnconfigure(6, weight=1)
        self.start_button = ttk.Button(actions, text="Start receiver", command=self.start_receiver)
        self.start_button.grid(row=0, column=0, padx=(0, 8))
        self.stop_button = ttk.Button(actions, text="Stop", command=self.stop_receiver, state="disabled")
        self.stop_button.grid(row=0, column=1, padx=(0, 16))
        ttk.Label(actions, text="Status").grid(row=0, column=2, sticky="w")
        ttk.Label(actions, textvariable=self.status).grid(row=0, column=3, sticky="w", padx=(8, 16))
        ttk.Label(actions, text="Session").grid(row=0, column=4, sticky="w")
        ttk.Label(actions, textvariable=self.session_label).grid(row=0, column=5, sticky="w", padx=(8, 16))
        self.process_warning = ttk.Label(actions, text="", foreground="#a15c00")
        self.process_warning.grid(row=0, column=6, sticky="e")

        main = ttk.PanedWindow(self, orient=tk.HORIZONTAL)
        main.grid(row=2, column=0, sticky="nsew", padx=12, pady=(0, 12))

        left = ttk.Frame(main)
        left.columnconfigure(0, weight=1)
        left.rowconfigure(2, weight=1)
        main.add(left, weight=1)

        stats = ttk.LabelFrame(left, text="Connection")
        stats.grid(row=0, column=0, sticky="ew", pady=(0, 8))
        stats.columnconfigure(1, weight=1)
        self.discovery_label = tk.StringVar(value="0")
        self.metadata_label = tk.StringVar(value="0")
        self.video_label = tk.StringVar(value="0")
        rows = [
            ("Discovery replies", self.discovery_label),
            ("Last headset", self.last_discovery_remote),
            ("Metadata packets", self.metadata_label),
            ("Last metadata remote", self.last_metadata_remote),
            ("Media packets", self.video_label),
            ("Last media remote", self.last_video_remote),
            ("Last frame", self.last_frame),
        ]
        for row, (name, var) in enumerate(rows):
            ttk.Label(stats, text=name).grid(row=row, column=0, sticky="w", padx=8, pady=3)
            ttk.Label(stats, textvariable=var).grid(row=row, column=1, sticky="w", padx=8, pady=3)

        summary = ttk.LabelFrame(left, text="Latest metadata summary")
        summary.grid(row=1, column=0, sticky="ew", pady=(0, 8))
        summary.columnconfigure(0, weight=1)
        self.summary_text = tk.Text(summary, height=9, wrap="word")
        self.summary_text.grid(row=0, column=0, sticky="ew", padx=8, pady=8)
        self.summary_text.configure(state="disabled")

        log_frame = ttk.LabelFrame(left, text="Receiver log")
        log_frame.grid(row=2, column=0, sticky="nsew")
        log_frame.columnconfigure(0, weight=1)
        log_frame.rowconfigure(0, weight=1)
        self.log_text = tk.Text(log_frame, height=12, wrap="none")
        self.log_text.grid(row=0, column=0, sticky="nsew")
        log_scroll = ttk.Scrollbar(log_frame, orient="vertical", command=self.log_text.yview)
        log_scroll.grid(row=0, column=1, sticky="ns")
        self.log_text.configure(yscrollcommand=log_scroll.set, state="disabled")

        right = ttk.PanedWindow(main, orient=tk.VERTICAL)
        main.add(right, weight=1)

        preview_frame = ttk.LabelFrame(right, text="Live JPEG media preview")
        preview_frame.columnconfigure(0, weight=1)
        preview_frame.rowconfigure(0, weight=1)
        self.preview_label = ttk.Label(preview_frame, anchor="center")
        self.preview_label.grid(row=0, column=0, sticky="nsew", padx=8, pady=8)
        ttk.Label(preview_frame, textvariable=self.preview_status).grid(row=1, column=0, sticky="ew", padx=8, pady=(0, 8))
        right.add(preview_frame, weight=1)

        metadata_frame = ttk.LabelFrame(right, text="Latest metadata JSON")
        metadata_frame.columnconfigure(0, weight=1)
        metadata_frame.rowconfigure(0, weight=1)
        self.metadata_text = tk.Text(metadata_frame, wrap="none")
        self.metadata_text.grid(row=0, column=0, sticky="nsew")
        meta_y = ttk.Scrollbar(metadata_frame, orient="vertical", command=self.metadata_text.yview)
        meta_y.grid(row=0, column=1, sticky="ns")
        meta_x = ttk.Scrollbar(metadata_frame, orient="horizontal", command=self.metadata_text.xview)
        meta_x.grid(row=1, column=0, sticky="ew")
        self.metadata_text.configure(yscrollcommand=meta_y.set, xscrollcommand=meta_x.set, state="disabled")
        right.add(metadata_frame, weight=1)

    def refresh_interfaces(self) -> None:
        interfaces = list_ipv4_interfaces()
        values = [f"{item['alias']} | {item['ip']}/{item['prefix']}" for item in interfaces]
        self.interface_values = values
        bind_values = ["0.0.0.0 (all interfaces)"] + values
        advertise_values = ["auto"] + values
        self.bind_combo.configure(values=bind_values)
        self.advertise_combo.configure(values=advertise_values)

        wlan = next((value for value in values if value.lower().startswith("wlan ") or "wlan |" in value.lower()), None)
        if wlan:
            self.advertise_value.set(wlan)
        elif values and self.advertise_value.get() == "auto":
            self.advertise_value.set(values[0])
        if self.bind_value.get() not in bind_values:
            self.bind_value.set("0.0.0.0 (all interfaces)")

    def refresh_process_warning(self) -> None:
        processes = find_existing_receiver_processes()
        if processes:
            self.process_warning.configure(text=f"Existing CLI receiver detected ({len(processes)})")
            self.append_log("Existing q3dc_receiver.py process may already own the UDP ports:")
            for process in processes:
                self.append_log(process)
        else:
            self.process_warning.configure(text="")

    def choose_output_dir(self) -> None:
        path = filedialog.askdirectory(initialdir=str(Path.cwd() / "captures"))
        if path:
            self.output_dir.set(path)

    def open_output_dir(self) -> None:
        path = self.current_session_dir or Path(self.output_dir.get())
        path.mkdir(parents=True, exist_ok=True)
        os.startfile(path)

    def start_receiver(self) -> None:
        if self.receiver is not None:
            return

        bind_ip = parse_combo_ip(self.bind_value.get()) or "0.0.0.0"
        advertise_ip = parse_combo_ip(self.advertise_value.get())
        try:
            discovery_port = int(self.discovery_port.get())
            metadata_port = int(self.metadata_port.get())
            video_port = int(self.video_port.get())
        except ValueError:
            messagebox.showerror("Invalid ports", "Ports must be integers.")
            return

        for port in (discovery_port, metadata_port, video_port):
            ok, error = check_udp_port_available(bind_ip, port)
            if not ok:
                messagebox.showerror(
                    "UDP port unavailable",
                    f"Cannot bind {bind_ip}:{port}\n\n{error}\n\nStop the old receiver process first.",
                )
                return

        self.discovery_count = 0
        self.metadata_count = 0
        self.video_count = 0
        self.discovery_label.set("0")
        self.metadata_label.set("0")
        self.video_label.set("0")
        self.status.set("Starting")

        capture_root = Path(self.output_dir.get())
        self.current_session_dir = None
        self.session_label.set("Waiting for headset recording")
        self.preview_status.set("Waiting for headset recording...")
        self.preview_label.configure(image="")
        self.preview_image = None
        self.receiver = Receiver(
            bind=bind_ip,
            advertise_ip=advertise_ip,
            discovery_port=discovery_port,
            metadata_port=metadata_port,
            video_port=video_port,
            out_dir=capture_root,
            on_log=lambda line: self.events.put(("log", line)),
            on_discovery=lambda item: self.events.put(("discovery", item)),
            on_metadata=lambda item: self.events.put(("metadata", item)),
            on_video=lambda item: self.events.put(("video", item)),
            on_session=lambda item: self.events.put(("session", item)),
            lazy_session=True,
            export_options=ExportOptions(
                task_name=self.task_name.get().strip() or "q3_data_collection",
                coordinate_space=self.coordinate_space.get(),
                include_headset_pose=self.include_headset_pose.get(),
                include_controller_buttons=self.include_controller_buttons.get(),
                include_camera_matrices=self.include_camera_matrices.get(),
                include_images=self.include_images.get(),
                write_frame_history=self.write_frame_history.get(),
            ),
        )
        self.receiver_thread = threading.Thread(target=self.run_receiver, daemon=True)
        self.receiver_thread.start()
        self.start_button.configure(state="disabled")
        self.stop_button.configure(state="normal")
        self.status.set("Listening")

    def run_receiver(self) -> None:
        try:
            assert self.receiver is not None
            self.receiver.run()
        except Exception as exc:
            self.events.put(("error", str(exc)))
        finally:
            self.events.put(("stopped", None))

    def stop_receiver(self) -> None:
        if self.receiver is not None:
            self.status.set("Stopping")
            self.receiver.stop_event.set()

    def drain_events(self) -> None:
        while True:
            try:
                kind, value = self.events.get_nowait()
            except queue.Empty:
                break

            if kind == "log":
                self.append_log(str(value))
            elif kind == "discovery":
                self.handle_discovery(value)
            elif kind == "metadata":
                self.handle_metadata(value)
            elif kind == "video":
                self.handle_video(value)
            elif kind == "session":
                self.handle_session(value)
            elif kind == "error":
                self.append_log(f"ERROR: {value}")
                messagebox.showerror("Receiver error", str(value))
            elif kind == "stopped":
                self.receiver = None
                self.receiver_thread = None
                self.start_button.configure(state="normal")
                self.stop_button.configure(state="disabled")
                self.status.set("Stopped")

        self.after(100, self.drain_events)

    def handle_session(self, item: object) -> None:
        data = item if isinstance(item, dict) else {}
        session_dir = data.get("sessionDir")
        if isinstance(session_dir, str):
            self.current_session_dir = Path(session_dir)
            self.session_label.set(self.current_session_dir.name)
            trigger = data.get("trigger", "-")
            remote = data.get("remote", "-")
            self.append_log(f"Session created from headset recording ({trigger}, {remote}): {session_dir}")

    def handle_discovery(self, item: object) -> None:
        data = item if isinstance(item, dict) else {}
        self.discovery_count += 1
        self.discovery_label.set(str(self.discovery_count))
        self.last_discovery_remote.set(str(data.get("remote", "-")))

    def handle_metadata(self, item: object) -> None:
        data = item if isinstance(item, dict) else {}
        self.metadata_count += 1
        self.metadata_label.set(str(self.metadata_count))
        self.last_metadata_remote.set(str(data.get("remote", "-")))
        payload = str(data.get("payload", ""))
        self.set_text(self.metadata_text, pretty_json(payload))
        self.set_text(self.summary_text, metadata_summary(payload))

    def handle_video(self, item: object) -> None:
        data = item if isinstance(item, dict) else {}
        self.video_count += 1
        self.video_label.set(str(self.video_count))
        self.last_video_remote.set(str(data.get("remote", "-")))
        header = data.get("header") if isinstance(data, dict) else None
        if isinstance(header, dict):
            packet_header = header.get("header", {})
            self.last_frame.set(str(packet_header.get("frameId", "-")))
        self.update_preview(data)

    def update_preview(self, data: dict[str, object]) -> None:
        if Image is None or ImageTk is None:
            self.show_preview_message("Pillow is not installed; JPEG preview is unavailable.")
            return

        file_path = data.get("absolutePath")
        if not file_path and self.current_session_dir is not None:
            relative = data.get("file")
            if isinstance(relative, str):
                file_path = str(self.current_session_dir / relative)

        if not isinstance(file_path, str) or not file_path.lower().endswith(".jpg"):
            media_kind = data.get("mediaKind", "media")
            self.show_preview_message(f"Latest {media_kind} packet is not a JPEG-previewable frame.")
            return

        try:
            image = Image.open(file_path)
            image.thumbnail((620, 360))
            photo = ImageTk.PhotoImage(image)
        except Exception as exc:
            self.show_preview_message(f"Could not preview frame: {exc}")
            return

        self.preview_image = photo
        self.preview_label.configure(image=photo)
        size = data.get("payloadByteLength", 0)
        media_kind = data.get("mediaKind", "media")
        self.preview_status.set(f"{media_kind}: {Path(file_path).name}  {size} bytes")

    def show_preview_message(self, message: str) -> None:
        self.preview_image = None
        self.preview_status.set(message)
        self.preview_label.configure(image="")

    def append_log(self, line: str) -> None:
        self.log_text.configure(state="normal")
        self.log_text.insert("end", line + "\n")
        self.log_text.see("end")
        self.log_text.configure(state="disabled")

    def set_text(self, widget: tk.Text, text: str) -> None:
        widget.configure(state="normal")
        widget.delete("1.0", "end")
        widget.insert("1.0", text)
        widget.configure(state="disabled")

    def on_close(self) -> None:
        if self.receiver is not None:
            self.receiver.stop_event.set()
            time.sleep(0.2)
        self.destroy()


def pretty_json(payload: str) -> str:
    try:
        return json.dumps(json.loads(payload), ensure_ascii=False, indent=2)
    except json.JSONDecodeError:
        return payload


def metadata_summary(payload: str) -> str:
    try:
        packet = json.loads(payload)
    except json.JSONDecodeError:
        return "Payload is not valid JSON."

    header = packet.get("header", {})
    snapshot = packet.get("snapshot", {})
    lines = [
        f"frameId: {header.get('frameId', snapshot.get('frameId', '-'))}",
        f"timestampUnixMs: {header.get('timestampUnixMs', snapshot.get('timestampUnixMs', '-'))}",
        f"sequenceId: {header.get('sequenceId', '-')}",
        f"status: {snapshot.get('status', '-')}",
        f"isSendable: {snapshot.get('isSendable', '-')}",
        f"hasCameraImage: {snapshot.get('hasCameraImage', '-')}",
        f"hasCameraPose: {snapshot.get('hasCameraPose', '-')}",
        f"hasCameraMetadata: {snapshot.get('hasCameraMetadata', '-')}",
        f"hasCameraStreamState: {snapshot.get('hasCameraStreamState', '-')}",
        f"hasController: {snapshot.get('hasController', '-')}",
        f"cameraImageDeltaMs: {snapshot.get('cameraImageTimeDeltaMs', '-')}",
        f"cameraPoseDeltaMs: {snapshot.get('cameraPoseTimeDeltaMs', '-')}",
        f"cameraMetadataDeltaMs: {snapshot.get('cameraMetadataTimeDeltaMs', '-')}",
        f"cameraStreamStateDeltaMs: {snapshot.get('cameraStreamStateTimeDeltaMs', '-')}",
        f"controllerDeltaMs: {snapshot.get('controllerTimeDeltaMs', '-')}",
    ]
    drop_reason = snapshot.get("dropReason")
    if drop_reason:
        lines.append(f"dropReason: {drop_reason}")

    controller = snapshot.get("controller")
    if isinstance(controller, dict):
        lines.append(f"controller frame: {controller.get('frameId', '-')}")

    camera_state = snapshot.get("cameraStreamState")
    if isinstance(camera_state, dict):
        lines.append(f"camera eye: {camera_state.get('cameraEye', '-')}")
        lines.append(f"camera fps: {camera_state.get('measuredFramerate', '-')}")

    return "\n".join(lines)


def main() -> None:
    app = ReceiverWindow()
    app.protocol("WM_DELETE_WINDOW", app.on_close)
    app.mainloop()


if __name__ == "__main__":
    sys.exit(main())
