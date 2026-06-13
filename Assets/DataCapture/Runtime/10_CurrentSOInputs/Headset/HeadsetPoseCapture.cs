using UnityEngine;
using DataCapture.Synchronization;

namespace DataCapture
{
    /// <summary>
    /// Captures headset pose data and writes to the current/queue SO data bus.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-40)]
    public class HeadsetPoseCapture : MonoBehaviour
    {
        [Header("Clock")]
        [SerializeField] private TimeStampVariable timestampVariable;

        [Header("Sources")]
        [SerializeField] private Transform centerEyeAnchor;
        [SerializeField] private bool autoResolveEyeAnchors = true;

        [Header("Coordinate Frames")]
        [SerializeField] private WorldCoordinateFrameSO worldCoordinateFrame;
        [SerializeField] private SessionCoordinateCalibrationSO sessionCalibration;

        [Header("Outputs")]
        [SerializeField] private CurrentHeadsetPoseSO currentPose;

        private void Awake()
        {
            ResolveCenterEyeAnchorFromTrackingSpace();
        }

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

            ResolveCenterEyeAnchorFromTrackingSpace();

            bool hasCenterEye = centerEyeAnchor != null;

            if (!hasCenterEye)
            {
                return;
            }

            Pose6DofRecord worldCenterEye = Pose6DofRecord.FromTransform(centerEyeAnchor);
            bool hasCalibration = TryGetCalibratedPose(worldCenterEye, out Pose6DofRecord calibratedCenterEye);

            var record = new HeadsetPoseRecord
            {
                timestampUnixMs = SynchronizationClock.GetUnixMilliseconds(timestampVariable),
                worldCenterEye = worldCenterEye,
                calibratedCenterEye = calibratedCenterEye,
                hasCenterEye = hasCenterEye,
                hasWorldCoordinateFrame = worldCoordinateFrame != null && worldCoordinateFrame.isValid,
                hasCalibration = hasCalibration,
                worldCoordinateFrameName = worldCoordinateFrame != null ? worldCoordinateFrame.name : string.Empty,
                calibratedCoordinateFrameName = hasCalibration && sessionCalibration != null
                    ? sessionCalibration.name
                    : string.Empty
            };

            currentPose.SetPose(record);
        }

        private bool TryGetCalibratedPose(Pose6DofRecord worldPose, out Pose6DofRecord calibratedPose)
        {
            if (sessionCalibration != null &&
                sessionCalibration.TryTransformWorldPose(worldPose, out calibratedPose))
            {
                return true;
            }

            calibratedPose = worldPose;
            return false;
        }

        private void ResolveCenterEyeAnchorFromTrackingSpace()
        {
            if (!autoResolveEyeAnchors || centerEyeAnchor != null)
            {
                return;
            }

            var cameraRig = FindAnyObjectByType<OVRCameraRig>();
            if (cameraRig != null && cameraRig.centerEyeAnchor != null)
            {
                centerEyeAnchor = cameraRig.centerEyeAnchor;
                return;
            }
        }

    }
}
