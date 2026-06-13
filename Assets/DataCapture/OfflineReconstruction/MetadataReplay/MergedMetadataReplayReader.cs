using System.IO;
using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.OfflineReconstruction
{
    public class MergedMetadataReplayReader : MonoBehaviour
    {
        public MergedFrameSnapshotRecord[] LoadFromJsonFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return new MergedFrameSnapshotRecord[0];
            }

            string json = File.ReadAllText(path);
            var payload = JsonUtility.FromJson<MergedFrameSnapshotList>(json);
            return payload != null && payload.records != null ? payload.records : new MergedFrameSnapshotRecord[0];
        }

        [System.Serializable]
        private class MergedFrameSnapshotList
        {
            public MergedFrameSnapshotRecord[] records;
        }
    }
}
