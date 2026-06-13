using UnityEngine;
using SObasic.CurrentQueueBridge;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "CurrentControllerPoseSO", menuName = "DataCapture/30 Pose Metadata Capture/Controller/Current Controller Pose")]
    public class CurrentControllerPoseSO : ScriptableObject, ICurrentRecordSource
    {
        public long timestampUnixMs;
        public bool isValid;

        [Header("Coordinate Frames")]
        public bool hasWorldCoordinateFrame;
        public bool hasCalibration;
        public string worldCoordinateFrameName;
        public string calibratedCoordinateFrameName;

        [Header("Left Controller Buttons")]
        public bool leftTriggerPressed;
        public bool leftGripPressed;
        public bool leftPrimaryButtonPressed;
        public bool leftSecondaryButtonPressed;

        [Header("Right Controller Buttons")]
        public bool rightTriggerPressed;
        public bool rightGripPressed;
        public bool rightPrimaryButtonPressed;
        public bool rightSecondaryButtonPressed;

        [Header("World Space")]
        public Vector3 worldLeftPosition;
        public Quaternion worldLeftRotation;
        public Vector3 worldRightPosition;
        public Quaternion worldRightRotation;

        [Header("Calibrated Space")]
        public Vector3 calibratedLeftPosition;
        public Quaternion calibratedLeftRotation;
        public Vector3 calibratedRightPosition;
        public Quaternion calibratedRightRotation;

        public string SourceName => name;
        public System.Type RecordType => typeof(ControllerPoseRecord);
        public bool IsRecordValid => isValid;
        public long CurrentTimestampUnixMs => timestampUnixMs;
        public long RecordSequence => timestampUnixMs;

        public void SetPose(ControllerPoseRecord record)
        {
            timestampUnixMs = record.timestampUnixMs;
            worldLeftPosition = record.worldLeftPosition;
            worldLeftRotation = record.worldLeftRotation;
            worldRightPosition = record.worldRightPosition;
            worldRightRotation = record.worldRightRotation;
            calibratedLeftPosition = record.hasCalibration ? record.calibratedLeftPosition : worldLeftPosition;
            calibratedLeftRotation = record.hasCalibration ? record.calibratedLeftRotation : worldLeftRotation;
            calibratedRightPosition = record.hasCalibration ? record.calibratedRightPosition : worldRightPosition;
            calibratedRightRotation = record.hasCalibration ? record.calibratedRightRotation : worldRightRotation;

            leftTriggerPressed = record.leftTriggerPressed;
            leftGripPressed = record.leftGripPressed;
            leftPrimaryButtonPressed = record.leftPrimaryButtonPressed;
            leftSecondaryButtonPressed = record.leftSecondaryButtonPressed;
            rightTriggerPressed = record.rightTriggerPressed;
            rightGripPressed = record.rightGripPressed;
            rightPrimaryButtonPressed = record.rightPrimaryButtonPressed;
            rightSecondaryButtonPressed = record.rightSecondaryButtonPressed;
            hasWorldCoordinateFrame = record.hasWorldCoordinateFrame;
            hasCalibration = record.hasCalibration;
            worldCoordinateFrameName = record.worldCoordinateFrameName;
            calibratedCoordinateFrameName = record.calibratedCoordinateFrameName;
            isValid = timestampUnixMs > 0;
            MarkDirtyInEditor();
        }

        public ControllerPoseRecord ToRecord()
        {
            return new ControllerPoseRecord
            {
                timestampUnixMs = timestampUnixMs,
                leftTriggerPressed = leftTriggerPressed,
                leftGripPressed = leftGripPressed,
                leftPrimaryButtonPressed = leftPrimaryButtonPressed,
                leftSecondaryButtonPressed = leftSecondaryButtonPressed,
                rightTriggerPressed = rightTriggerPressed,
                rightGripPressed = rightGripPressed,
                rightPrimaryButtonPressed = rightPrimaryButtonPressed,
                rightSecondaryButtonPressed = rightSecondaryButtonPressed,
                worldLeftPosition = worldLeftPosition,
                worldLeftRotation = worldLeftRotation,
                worldRightPosition = worldRightPosition,
                worldRightRotation = worldRightRotation,
                calibratedLeftPosition = calibratedLeftPosition,
                calibratedLeftRotation = calibratedLeftRotation,
                calibratedRightPosition = calibratedRightPosition,
                calibratedRightRotation = calibratedRightRotation,
                hasWorldCoordinateFrame = hasWorldCoordinateFrame,
                hasCalibration = hasCalibration,
                worldCoordinateFrameName = worldCoordinateFrameName,
                calibratedCoordinateFrameName = calibratedCoordinateFrameName
            };
        }

        public bool TryGetRecord(out ITimestampedData record)
        {
            if (!isValid)
            {
                record = null;
                return false;
            }

            record = ToRecord();
            return true;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void MarkDirtyInEditor()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(this);
            }
#endif
        }
    }
}
