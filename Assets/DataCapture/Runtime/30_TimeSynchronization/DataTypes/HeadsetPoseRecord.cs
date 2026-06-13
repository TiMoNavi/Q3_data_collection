namespace DataCapture.Synchronization
{
    [System.Serializable]
    public struct HeadsetPoseRecord : ITimestampedData
    {
        public long timestampUnixMs;

        public Pose6DofRecord worldCenterEye;
        public Pose6DofRecord calibratedCenterEye;
        public bool hasCenterEye;
        public bool hasWorldCoordinateFrame;
        public bool hasCalibration;
        public string worldCoordinateFrameName;
        public string calibratedCoordinateFrameName;

        public long Timestamp => timestampUnixMs;
        public bool HasCenterEyePose => hasCenterEye;
    }
}
