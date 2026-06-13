using UnityEngine;

namespace DataCapture.Synchronization
{
    [System.Serializable]
    public struct ControllerPoseRecord : ITimestampedData
    {
        public long timestampUnixMs;

        public bool leftTriggerPressed;
        public bool leftGripPressed;
        public bool leftPrimaryButtonPressed;
        public bool leftSecondaryButtonPressed;

        public bool rightTriggerPressed;
        public bool rightGripPressed;
        public bool rightPrimaryButtonPressed;
        public bool rightSecondaryButtonPressed;

        public Vector3 worldLeftPosition;
        public Quaternion worldLeftRotation;
        public Vector3 worldRightPosition;
        public Quaternion worldRightRotation;
        public Vector3 calibratedLeftPosition;
        public Quaternion calibratedLeftRotation;
        public Vector3 calibratedRightPosition;
        public Quaternion calibratedRightRotation;
        public bool hasWorldCoordinateFrame;
        public bool hasCalibration;
        public string worldCoordinateFrameName;
        public string calibratedCoordinateFrameName;

        public long Timestamp => timestampUnixMs;
    }
}
