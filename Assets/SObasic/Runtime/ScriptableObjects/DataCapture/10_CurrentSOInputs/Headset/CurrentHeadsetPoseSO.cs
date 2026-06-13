using UnityEngine;
using SObasic.CurrentQueueBridge;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "CurrentHeadsetPoseSO", menuName = "DataCapture/30 Pose Metadata Capture/Headset/Current Headset Pose")]
    public class CurrentHeadsetPoseSO : ScriptableObject, ICurrentRecordSource
    {
        public long timestampUnixMs;
        public bool isValid;

        [Header("Coordinate Frames")]
        public bool hasWorldCoordinateFrame;
        public bool hasCalibration;
        public string worldCoordinateFrameName;
        public string calibratedCoordinateFrameName;

        [Header("Tracking State")]
        public bool hasCenterEye;

        [Header("World Space")]
        public Pose6DofRecord worldCenterEye;

        [Header("Calibrated Space")]
        public Pose6DofRecord calibratedCenterEye;

        public string SourceName => name;
        public System.Type RecordType => typeof(HeadsetPoseRecord);
        public bool IsRecordValid => isValid;
        public long CurrentTimestampUnixMs => timestampUnixMs;
        public long RecordSequence => timestampUnixMs;

        public void SetPose(HeadsetPoseRecord record)
        {
            timestampUnixMs = record.timestampUnixMs;

            worldCenterEye = record.worldCenterEye;
            calibratedCenterEye = record.hasCalibration ? record.calibratedCenterEye : worldCenterEye;

            hasCenterEye = record.hasCenterEye;
            hasWorldCoordinateFrame = record.hasWorldCoordinateFrame;
            hasCalibration = record.hasCalibration;
            worldCoordinateFrameName = record.worldCoordinateFrameName;
            calibratedCoordinateFrameName = record.calibratedCoordinateFrameName;
            isValid = timestampUnixMs > 0 && record.HasCenterEyePose;
            MarkDirtyInEditor();
        }

        public HeadsetPoseRecord ToRecord()
        {
            return new HeadsetPoseRecord
            {
                timestampUnixMs = timestampUnixMs,
                worldCenterEye = worldCenterEye,
                calibratedCenterEye = calibratedCenterEye,
                hasCenterEye = hasCenterEye,
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
