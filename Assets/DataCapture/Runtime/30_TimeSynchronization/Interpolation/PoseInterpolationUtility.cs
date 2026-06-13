using UnityEngine;

namespace DataCapture.Synchronization
{
    public static class PoseInterpolationUtility
    {
        public static Pose Lerp(Pose a, Pose b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Pose(
                Vector3.LerpUnclamped(a.position, b.position, t),
                Quaternion.SlerpUnclamped(a.rotation, b.rotation, t));
        }

        public static HeadsetPoseRecord Lerp(HeadsetPoseRecord a, HeadsetPoseRecord b, long targetTimestamp)
        {
            float t = GetInterpolationT(a.timestampUnixMs, b.timestampUnixMs, targetTimestamp);
            return new HeadsetPoseRecord
            {
                timestampUnixMs = targetTimestamp,
                worldCenterEye = Lerp(a.worldCenterEye, b.worldCenterEye, t),
                calibratedCenterEye = Lerp(a.calibratedCenterEye, b.calibratedCenterEye, t),
                hasCenterEye = a.hasCenterEye && b.hasCenterEye,
                hasWorldCoordinateFrame = a.hasWorldCoordinateFrame && b.hasWorldCoordinateFrame,
                hasCalibration = a.hasCalibration && b.hasCalibration,
                worldCoordinateFrameName = a.worldCoordinateFrameName == b.worldCoordinateFrameName
                    ? a.worldCoordinateFrameName
                    : string.Empty,
                calibratedCoordinateFrameName = a.calibratedCoordinateFrameName == b.calibratedCoordinateFrameName
                    ? a.calibratedCoordinateFrameName
                    : string.Empty
            };
        }

        public static Pose6DofRecord Lerp(Pose6DofRecord a, Pose6DofRecord b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Pose6DofRecord(
                Vector3.LerpUnclamped(a.position, b.position, t),
                Quaternion.SlerpUnclamped(a.rotation, b.rotation, t));
        }

        private static float GetInterpolationT(long startTimestamp, long endTimestamp, long targetTimestamp)
        {
            if (endTimestamp == startTimestamp)
            {
                return 0f;
            }

            return Mathf.Clamp01((targetTimestamp - startTimestamp) / (float)(endTimestamp - startTimestamp));
        }
    }
}
