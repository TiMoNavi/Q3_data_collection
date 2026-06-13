# Coordinate System Calibration Design

> Current stage: documentation only. This document does not implement code changes, create ScriptableObject assets, move scene objects, or modify Unity scene wiring.

## Purpose

This project collects headset and controller 6DoF data on Meta Quest for downstream robot-training workflows. The current headset and controller capture flow records Unity world-space poses directly. That is useful for debugging inside the Unity scene, but it can mix the user's session start position and facing direction into the training data.

For robot training, the same human motion should ideally be represented in a consistent session-centric coordinate system:

- The origin is under the player at the start of capture.
- +Y is Unity world up.
- +Z is the player's chosen forward direction at calibration time.
- +X is the player's right direction.

The goal of the future coordinate calibration flow is to keep raw world-space data available while also producing calibrated 6DoF data relative to this session coordinate system. Both coordinate systems should have explicit SO state, so downstream capture, export, debugging, and training tools can know which frame each pose belongs to.

## Current Capture Flow

The current DataCapture flow for headset and controller 6DoF is:

```text
HeadsetPoseCapture / ControllerPoseCapture / ControllerButtonCapture
    -> CurrentHeadsetPoseSO / CurrentControllerPoseSO
    -> CurrentToQueueRecorder, gated by RecordingGateSO
    -> HeadsetPoseQueueSO / ControllerPoseQueueSO
    -> TimestampMerger
    -> MergedFrameSnapshotRecord
    -> JSON export or metadata network send
```

Important current files:

- `Assets/DataCapture/Runtime/10_CurrentSOInputs/Headset/HeadsetPoseCapture.cs`
- `Assets/DataCapture/Runtime/10_CurrentSOInputs/Controller/ControllerPoseCapture.cs`
- `Assets/DataCapture/Runtime/10_CurrentSOInputs/Controller/ControllerButtonCapture.cs`
- `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/10_CurrentSOInputs/Headset/CurrentHeadsetPoseSO.cs`
- `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/10_CurrentSOInputs/Controller/CurrentControllerPoseSO.cs`
- `Assets/SObasic/Runtime/CurrentQueueBridge/CurrentToQueueRecorder.cs`
- `Assets/DataCapture/Runtime/30_TimeSynchronization/Sync/TimestampMerger.cs`

`HeadsetPoseCapture` reads the center/left/right eye anchor transforms and writes their `Transform.position` and `Transform.rotation`. `ControllerPoseCapture` does the same for the left and right hand/controller anchors when those anchors are assigned. These values are Unity world-space values at the time of capture.

`ControllerButtonCapture` writes button states into the same controller current SO. It does not define pose coordinates itself, but it shares the controller record with the pose capture.

`CurrentToQueueRecorder` records the current SO values into queue SOs while the recording gate is active. `TimestampMerger` then aligns camera, headset, controller, and other queues by timestamp. It does not transform coordinate systems.

## Current Problem

Because headset and controller poses are recorded directly in Unity world space, the recorded 6DoF data depends on the current Quest tracking/world-space origin and orientation.

Example:

```text
At capture start:
Player feet are near world position (1.8, 0.0, -0.6)
Player is facing diagonally relative to Unity +Z
```

In this case:

- The initial headset pose will start around `(1.8, headHeight, -0.6)` instead of around `(0, headHeight, 0)`.
- The same reaching motion can have different absolute positions across sessions.
- A human "forward" motion can map to different Unity world directions across sessions.
- Robot-training data may learn room/tracking-space offsets rather than the human-centered action.

This is especially important when comparing multiple demonstrations or training a policy that expects actions in a consistent body/session coordinate frame.

## Meta XR Rig Relationship

Meta's Unity documentation describes `OVRCameraRig` as the Meta XR rig. Its `TrackingSpace` child contains the eye anchors and controller anchors, including `CenterEyeAnchor`, `LeftHandAnchor`, and `RightHandAnchor`.

The tracked headset and controller pose values are updated by Meta XR tracking. For this reason, the calibration flow should not directly overwrite `CenterEyeAnchor`, hand anchor, or controller anchor local transforms.

The safer approach is:

- Leave Meta XR tracking anchors under `TrackingSpace` unchanged.
- Read their world-space poses as the raw source data.
- Define a separate calibration transform/matrix in data space.
- Convert world-space poses into calibrated session-space poses before recording or while writing records.

Official Meta documentation referenced for this design:

- Configure Meta XR camera settings: https://developers.meta.com/horizon/documentation/unity/unity-ovrcamerarig/
- Develop Unity apps for Meta Quest VR headsets: https://developers.meta.com/horizon/documentation/unity/unity-development-overview/

## Calibration Coordinate System

The future calibrated coordinate system should be created by a calibration/reset step before data capture starts.

Definition:

- `originWorld`: the player's foot position at calibration time.
- `upWorld`: `Vector3.up`.
- `forwardWorld`: the horizontal projection of `CenterEyeAnchor.forward`.
- `rightWorld`: the right direction derived from `upWorld` and `forwardWorld`.

The foot position should use the center eye's X/Z position and a floor height. For the current project, the default floor height can be `0` because the project is expected to use a floor-level tracking origin.

Conceptually:

```text
originWorld = (centerEye.position.x, 0, centerEye.position.z)
forwardWorld = Normalize(ProjectOnPlane(centerEye.forward, Vector3.up))
rotationWorld = Quaternion.LookRotation(forwardWorld, Vector3.up)
worldToCalibration = inverse(TRS(originWorld, rotationWorld, Vector3.one))
calibrationToWorld = TRS(originWorld, rotationWorld, Vector3.one)
```

Pose conversion:

```text
calibratedPosition = inverse(rotationWorld) * (worldPosition - originWorld)
calibratedRotation = inverse(rotationWorld) * worldRotation
```

After calibration, expected values are:

- Headset X/Z starts near zero.
- Headset Y starts near the user's real head height.
- Looking forward at calibration time maps to calibrated +Z.
- Left/right controller positions become relative to the player's calibrated body/session frame.

## SO Event And Coordinate State Design

The future implementation should use the existing SO architecture for both event control and coordinate-frame state. There should be one bool SO event for calibration reset and two coordinate-state SOs: one for Unity/Quest world space and one for the calibrated session space.

Suggested event asset name:

```text
CoordinateCalibrationResetRequest
```

Suggested behavior:

1. A UI, controller button, debug tool, or inspector action sets `CoordinateCalibrationResetRequest.Value = true`.
2. A calibration controller detects the rising edge.
3. The controller reads `CenterEyeAnchor`.
4. The controller updates the world coordinate SO and the calibrated coordinate SO.
5. The controller automatically clears `CoordinateCalibrationResetRequest.Value = false`.

Suggested world coordinate state asset name:

```text
WorldCoordinateFrame
```

Suggested world coordinate state fields:

- `bool isValid`
- `long updatedAtUnixMs`
- `Vector3 originWorld`
- `Vector3 upWorld`
- `Vector3 forwardWorld`
- `Quaternion rotationWorld`
- `Matrix4x4 localToWorldMatrix`
- `Matrix4x4 worldToLocalMatrix`
- `string description`

For the current Unity/Quest world frame, this SO will usually describe the identity world frame:

```text
originWorld = (0, 0, 0)
upWorld = Vector3.up
forwardWorld = Vector3.forward
rotationWorld = Quaternion.identity
localToWorldMatrix = Matrix4x4.identity
worldToLocalMatrix = Matrix4x4.identity
```

This may feel redundant, but it is useful because exported data can explicitly say, "these world poses are in this world coordinate frame."

Suggested calibrated coordinate state asset name:

```text
SessionCoordinateCalibration
```

Suggested calibrated coordinate state fields:

- `bool isCalibrated`
- `long calibratedAtUnixMs`
- `Vector3 originWorld`
- `Vector3 upWorld`
- `Vector3 forwardWorld`
- `Quaternion rotationWorld`
- `Matrix4x4 worldToCalibrationMatrix`
- `Matrix4x4 calibrationToWorldMatrix`
- `string description`

The world coordinate SO and calibrated coordinate SO should both be inspectable, exportable, and reusable by other systems. The bool SO remains only an event trigger; it should not store coordinate-frame state.

## Data Model Recommendation

Future headset and controller Current SOs and record structs should keep both coordinate meanings explicit.

Recommended policy:

- Existing world-space fields remain available.
- New calibrated/session-space fields are added.
- A record-level flag indicates whether calibrated data is valid.
- Current SOs and exported records should make it clear that world-space poses belong to `WorldCoordinateFrame`, and calibrated poses belong to `SessionCoordinateCalibration`.

For headset data:

```text
worldCenterEye / calibratedCenterEye
worldLeftEye / calibratedLeftEye
worldRightEye / calibratedRightEye
hasCalibration
```

For controller data:

```text
worldLeftPosition / worldLeftRotation
calibratedLeftPosition / calibratedLeftRotation
worldRightPosition / worldRightRotation
calibratedRightPosition / calibratedRightRotation
hasCalibration
```

The exact field names can be finalized during implementation. The important rule is that the old world-space meaning and the new calibrated-space meaning should not be silently mixed into the same field names.

## Passthrough Camera Note

This document focuses on headset and controller 6DoF. The passthrough camera flow currently also records camera pose and matrices in world space.

If robot training later consumes passthrough camera extrinsics, then the camera frame records should also be transformed or extended with calibrated/session-space extrinsics. That is intentionally out of scope for the first headset/controller calibration step, but it should not be forgotten.

## Implementation Boundary For This Stage

This stage is documentation only.

Do not change in this stage:

- `.cs` scripts
- `.asset` ScriptableObject assets
- `.unity` scenes
- prefab or inspector wiring
- file placement

Future implementation can use this document as the design reference for adding the calibration SOs, calibration controller, and updated headset/controller 6DoF data records.
