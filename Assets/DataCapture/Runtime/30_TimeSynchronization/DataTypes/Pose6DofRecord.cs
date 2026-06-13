using UnityEngine;

namespace DataCapture.Synchronization
{
    [System.Serializable]
    public struct Pose6DofRecord
    {
        public Vector3 position;
        public Quaternion rotation;

        public Pose6DofRecord(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }

        public static Pose6DofRecord FromTransform(Transform source)
        {
            return source != null
                ? new Pose6DofRecord(source.position, source.rotation)
                : new Pose6DofRecord(Vector3.zero, Quaternion.identity);
        }
    }
}
