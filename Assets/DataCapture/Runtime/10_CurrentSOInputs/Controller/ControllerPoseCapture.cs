using UnityEngine;
using DataCapture.Synchronization;

namespace DataCapture
{
    /// <summary>
    /// Captures controller pose data and writes to the current/queue SO data bus.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-40)]
    public class ControllerPoseCapture : MonoBehaviour
    {
        [Header("Clock")]
        [SerializeField] private TimeStampVariable timestampVariable;

        [Header("Sources")]
        [SerializeField] private Transform leftHandAnchor;
        [SerializeField] private Transform rightHandAnchor;
        [SerializeField] private bool fallbackToOVRInputWhenAnchorMissing = true;

        [Header("Coordinate Frames")]
        [SerializeField] private WorldCoordinateFrameSO worldCoordinateFrame;
        [SerializeField] private SessionCoordinateCalibrationSO sessionCalibration;

        [Header("Outputs")]
        [SerializeField] private CurrentControllerPoseSO currentPose;

        private void LateUpdate()
        {
            CaptureNow();
        }

        [ContextMenu("Capture Now")]
        public void CaptureNow()
        {
            if (currentPose == null)
            {
                return;
            }

            ControllerPoseRecord record = currentPose.isValid ? currentPose.ToRecord() : new ControllerPoseRecord();
            record.timestampUnixMs = SynchronizationClock.GetUnixMilliseconds(timestampVariable);

            Vector3 worldLeftPosition = GetPosition(leftHandAnchor, OVRInput.Controller.LTouch);
            Quaternion worldLeftRotation = GetRotation(leftHandAnchor, OVRInput.Controller.LTouch);
            Vector3 worldRightPosition = GetPosition(rightHandAnchor, OVRInput.Controller.RTouch);
            Quaternion worldRightRotation = GetRotation(rightHandAnchor, OVRInput.Controller.RTouch);
            bool hasCalibration = TryGetCalibratedPose(
                worldLeftPosition,
                worldLeftRotation,
                out Vector3 calibratedLeftPosition,
                out Quaternion calibratedLeftRotation);
            TryGetCalibratedPose(
                worldRightPosition,
                worldRightRotation,
                out Vector3 calibratedRightPosition,
                out Quaternion calibratedRightRotation);

            record.worldLeftPosition = worldLeftPosition;
            record.worldLeftRotation = worldLeftRotation;
            record.worldRightPosition = worldRightPosition;
            record.worldRightRotation = worldRightRotation;
            record.calibratedLeftPosition = calibratedLeftPosition;
            record.calibratedLeftRotation = calibratedLeftRotation;
            record.calibratedRightPosition = calibratedRightPosition;
            record.calibratedRightRotation = calibratedRightRotation;
            record.hasWorldCoordinateFrame = worldCoordinateFrame != null && worldCoordinateFrame.isValid;
            record.hasCalibration = hasCalibration;
            record.worldCoordinateFrameName = worldCoordinateFrame != null ? worldCoordinateFrame.name : string.Empty;
            record.calibratedCoordinateFrameName = hasCalibration && sessionCalibration != null
                ? sessionCalibration.name
                : string.Empty;

            currentPose.SetPose(record);
        }

        private bool TryGetCalibratedPose(
            Vector3 worldPosition,
            Quaternion worldRotation,
            out Vector3 calibratedPosition,
            out Quaternion calibratedRotation)
        {
            if (sessionCalibration != null && sessionCalibration.isCalibrated)
            {
                calibratedPosition = sessionCalibration.TransformWorldPosition(worldPosition);
                calibratedRotation = sessionCalibration.TransformWorldRotation(worldRotation);
                return true;
            }

            calibratedPosition = worldPosition;
            calibratedRotation = worldRotation;
            return false;
        }

        private Vector3 GetPosition(Transform anchor, OVRInput.Controller controller)
        {
            if (anchor != null)
            {
                return anchor.position;
            }

            return fallbackToOVRInputWhenAnchorMissing
                ? OVRInput.GetLocalControllerPosition(controller)
                : Vector3.zero;
        }

        private Quaternion GetRotation(Transform anchor, OVRInput.Controller controller)
        {
            if (anchor != null)
            {
                return anchor.rotation;
            }

            return fallbackToOVRInputWhenAnchorMissing
                ? OVRInput.GetLocalControllerRotation(controller)
                : Quaternion.identity;
        }
    }
}
