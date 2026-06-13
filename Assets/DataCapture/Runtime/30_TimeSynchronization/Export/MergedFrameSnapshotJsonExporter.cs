using System;
using System.IO;
using UnityEngine;

namespace DataCapture.Synchronization
{
    public class MergedFrameSnapshotJsonExporter : MonoBehaviour
    {
        [SerializeField] private MergedFrameSnapshotQueueSO sourceQueue;
        [SerializeField] private string exportFolder = "DataCaptureDebug/MergedSnapshots";

        [ContextMenu("Export Snapshot Queue")]
        public string ExportSnapshotQueue()
        {
            if (sourceQueue == null)
            {
                return string.Empty;
            }

            MergedFrameSnapshotRecord[] records = sourceQueue.ExportSnapshot();
            var payload = new MergedFrameSnapshotList
            {
                exportedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                count = records.Length,
                records = records
            };

            string folder = Path.Combine(Application.persistentDataPath, exportFolder);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_merged_snapshots.json");
            File.WriteAllText(path, JsonUtility.ToJson(payload, true));
            Debug.Log("Merged snapshot queue exported to: " + path, this);
            return path;
        }

        [Serializable]
        private class MergedFrameSnapshotList
        {
            public long exportedAtUnixMs;
            public int count;
            public MergedFrameSnapshotRecord[] records;
        }
    }
}
