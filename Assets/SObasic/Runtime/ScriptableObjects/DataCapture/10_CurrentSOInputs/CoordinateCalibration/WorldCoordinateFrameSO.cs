using UnityEngine;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(
        fileName = "WorldCoordinateFrameSO",
        menuName = "DataCapture/Coordinate System Calibration/World Coordinate Frame")]
    public class WorldCoordinateFrameSO : ScriptableObject
    {
        public bool isValid = true;
        public long updatedAtUnixMs;
        public Vector3 originWorld = Vector3.zero;
        public Vector3 upWorld = Vector3.up;
        public Vector3 forwardWorld = Vector3.forward;
        public Quaternion rotationWorld = Quaternion.identity;
        public Matrix4x4 localToWorldMatrix = Matrix4x4.identity;
        public Matrix4x4 worldToLocalMatrix = Matrix4x4.identity;
        public string description = "Unity/Quest world coordinate frame.";

        public void SetIdentity(long timestampUnixMs)
        {
            isValid = true;
            updatedAtUnixMs = timestampUnixMs;
            originWorld = Vector3.zero;
            upWorld = Vector3.up;
            forwardWorld = Vector3.forward;
            rotationWorld = Quaternion.identity;
            RefreshMatrices();
            MarkDirtyInEditor();
        }

        public void SetFrame(
            Vector3 origin,
            Quaternion rotation,
            long timestampUnixMs,
            string frameDescription = null)
        {
            isValid = true;
            updatedAtUnixMs = timestampUnixMs;
            originWorld = origin;
            rotationWorld = NormalizeOrIdentity(rotation);
            upWorld = rotationWorld * Vector3.up;
            forwardWorld = rotationWorld * Vector3.forward;

            if (!string.IsNullOrEmpty(frameDescription))
            {
                description = frameDescription;
            }

            RefreshMatrices();
            MarkDirtyInEditor();
        }

        private void OnValidate()
        {
            rotationWorld = NormalizeOrIdentity(rotationWorld);
            if (upWorld.sqrMagnitude <= Mathf.Epsilon)
            {
                upWorld = Vector3.up;
            }

            if (forwardWorld.sqrMagnitude <= Mathf.Epsilon)
            {
                forwardWorld = Vector3.forward;
            }

            upWorld.Normalize();
            forwardWorld.Normalize();
            RefreshMatrices();
        }

        private void RefreshMatrices()
        {
            localToWorldMatrix = Matrix4x4.TRS(originWorld, rotationWorld, Vector3.one);
            worldToLocalMatrix = localToWorldMatrix.inverse;
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
    }
}
