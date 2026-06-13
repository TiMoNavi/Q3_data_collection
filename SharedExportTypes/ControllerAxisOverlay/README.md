# Controller Axis Overlay Export

This folder is a shareable data contract for the offline Quest controller-axis overlay output.

It describes one exported JSON document that links saved PCReceiver image frames to:

- Quest Passthrough Camera API camera pose, intrinsics, and matrices.
- Left/right controller world-space pose.
- Projected 2D axis pixels used for drawing the red/green/blue XYZ overlay.

The contract is intentionally outside `Assets/` and `PCReceiver/` so it can be shared without the Unity project.

## Files

- `controller_axis_overlay_export.schema.json`: JSON Schema for validation.
- `controller_axis_overlay_export.example.json`: Small example document.
- `controller_axis_overlay_export.d.ts`: TypeScript type definitions.
- `ControllerAxisOverlayExport.cs`: Plain C# DTOs with no UnityEngine dependency.

## Version

Current schema version: `controller-axis-overlay/v1`.

Breaking changes should use a new schema version, for example `controller-axis-overlay/v2`.

## Coordinate Rules

- Units are meters for 3D world data.
- World-space data uses the Unity/Quest tracking world captured by the project.
- Camera data must come from Quest Passthrough Camera API metadata, not `CenterEyeAnchor`.
- Image pixels use top-left origin: `x` right, `y` down.
- Axes use the controller local rotation:
  - X: red
  - Y: green
  - Z: blue

## Minimal JSON Shape

```json
{
  "schemaVersion": "controller-axis-overlay/v1",
  "coordinateSystem": "unity-world-meters",
  "cameraModel": "quest-pca-pinhole",
  "axisLengthMeters": 0.12,
  "frames": []
}
```

## Recommended Validation

Use any JSON Schema 2020-12 validator against:

```text
controller_axis_overlay_export.schema.json
```

Frame records may include skipped controllers. A skipped controller has `isValid=false` and a `skipReason`, for example `controller_world_pose_invalid`.

## Current Producer

The matching and drawing logic lives in:

```text
PCReceiver/offline_controller_axis_overlay.py
```

The first implementation writes overlay images. If export JSON generation is added, it should emit this schema without changing the meaning of the existing metadata.
