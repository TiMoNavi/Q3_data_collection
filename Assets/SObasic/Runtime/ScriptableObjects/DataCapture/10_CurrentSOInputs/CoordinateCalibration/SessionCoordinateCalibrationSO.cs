using UnityEngine;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(
        fileName = "SessionCoordinateCalibrationSO",
        menuName = "DataCapture/Coordinate System Calibration/Session Coordinate Calibration")]
    public class SessionCoordinateCalibrationSO : ScriptableObject
    {
        public bool isCalibrated;
        public long calibratedAtUnixMs;
        public Vector3 originWorld = Vector3.zero;
        public Vector3 upWorld = Vector3.up;
        public Vector3 forwardWorld = Vector3.forward;
        public Quaternion rotationWorld = Quaternion.identity;
        public Matrix4x4 worldToCalibrationMatrix = Matrix4x4.identity;
        public Matrix4x4 calibrationToWorldMatrix = Matrix4x4.identity;
        public string description = "Human/session-centric coordinate frame.";

        public bool CalibrateFromCenterEye(Transform centerEyeAnchor, float floorWorldY, long timestampUnixMs)
        {
            if (centerEyeAnchor == null)
            {
                return false;
            }

            Pose6DofRecord centerEyePose = Pose6DofRecord.FromTransform(centerEyeAnchor);
            return CalibrateFromCenterEyePose(centerEyePose, floorWorldY, timestampUnixMs);
        }

        public bool CalibrateFromCenterEyePose(
            Pose6DofRecord centerEyeWorldPose,
            float floorWorldY,
            long timestampUnixMs)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(
                centerEyeWorldPose.rotation * Vector3.forward,
                Vector3.up);

            if (flatForward.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            originWorld = new Vector3(
                centerEyeWorldPose.position.x,
                floorWorldY,
                centerEyeWorldPose.position.z);
            upWorld = Vector3.up;
            forwardWorld = flatForward.normalized;
            rotationWorld = Quaternion.LookRotation(forwardWorld, upWorld);
            calibratedAtUnixMs = timestampUnixMs;
            isCalibrated = true;
            RefreshMatrices();
            MarkDirtyInEditor();
            return true;
        }

        public void SetFrame(
            Vector3 origin,
            Quaternion rotation,
            long timestampUnixMs,
            string frameDescription = null)
        {
            originWorld = origin;
            rotationWorld = NormalizeOrIdentity(rotation);
            upWorld = rotationWorld * Vector3.up;
            forwardWorld = rotationWorld * Vector3.forward;
            calibratedAtUnixMs = timestampUnixMs;
            isCalibrated = true;

            if (!string.IsNullOrEmpty(frameDescription))
            {
                description = frameDescription;
            }

            RefreshMatrices();
            MarkDirtyInEditor();
        }

        public void ClearCalibration()
        {
            isCalibrated = false;
            calibratedAtUnixMs = 0;
            originWorld = Vector3.zero;
            upWorld = Vector3.up;
            forwardWorld = Vector3.forward;
            rotationWorld = Quaternion.identity;
            RefreshMatrices();
            MarkDirtyInEditor();
        }

        public bool TryTransformWorldPose(Pose6DofRecord worldPose, out Pose6DofRecord calibratedPose)
        {
            if (!isCalibrated)
            {
                calibratedPose = worldPose;
                return false;
            }

            calibratedPose = TransformWorldPose(worldPose);
            return true;
        }

        public Pose6DofRecord TransformWorldPose(Pose6DofRecord worldPose)
        {
            Quaternion inverseRotation = Quaternion.Inverse(rotationWorld);
            return new Pose6DofRecord(
                inverseRotation * (worldPose.position - originWorld),
                inverseRotation * worldPose.rotation);
        }

        public Vector3 TransformWorldPosition(Vector3 worldPosition)
        {
            return worldToCalibrationMatrix.MultiplyPoint3x4(worldPosition);
        }

        public Quaternion TransformWorldRotation(Quaternion worldRotation)
        {
            return Quaternion.Inverse(rotationWorld) * worldRotation;
        }

        private void OnValidate()
        {
            if (upWorld.sqrMagnitude <= Mathf.Epsilon)
            {
                upWorld = Vector3.up;
            }

            if (forwardWorld.sqrMagnitude <= Mathf.Epsilon)
            {
                forwardWorld = Vector3.forward;
            }

            upWorld.Normalize();
            forwardWorld = Vector3.ProjectOnPlane(forwardWorld, upWorld);
            if (forwardWorld.sqrMagnitude <= Mathf.Epsilon)
            {
                forwardWorld = Vector3.forward;
            }

            forwardWorld.Normalize();
            rotationWorld = Quaternion.LookRotation(forwardWorld, upWorld);
            RefreshMatrices();
        }

        private void RefreshMatrices()
        {
            calibrationToWorldMatrix = Matrix4x4.TRS(originWorld, rotationWorld, Vector3.one);
            worldToCalibrationMatrix = calibrationToWorldMatrix.inverse;
        }

        private static Quaternion NormalizeOrIdentity(Quaternion rotation)
        {
            float lengthSquared =
                rotation.x * rotation.x +
                rotation.y * rotation.y +
                rotation.z * rotation.z +
                rotation.w * rotation.w;

            if (lengthSquared <= Mathf.Epsilon)
            {
                return Quaternion.identity;
            }

            float invLength = 1f / Mathf.Sqrt(lengthSquared);
            return new Quaternion(
                rotation.x * invLength,
                rotation.y * invLength,
                rotation.z * invLength,
                rotation.w * invLength);
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
