using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace DataCapture.Synchronization
{
    public class QueueSnapshotCsvExporter : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private CameraFrameTimingQueueSO cameraTimingQueue;
        [SerializeField] private CameraImageQueueSO cameraImageQueue;
        [SerializeField] private CameraPoseQueueSO cameraPoseQueue;
        [SerializeField] private CameraMetadataQueueSO cameraMetadataQueue;
        [SerializeField] private CameraStreamStateQueueSO cameraStreamStateQueue;
        [SerializeField] private ControllerPoseQueueSO controllerQueue;
        [SerializeField] private HeadsetPoseQueueSO headsetQueue;
        [SerializeField] private NetworkDeviceQueueSO networkQueue;
        [SerializeField] private VirtualLayerQueueSO virtualLayerQueue;

        [Header("Export")]
        [SerializeField] private string exportFolder = "DataCaptureDebug/QueueSnapshots";

        [ContextMenu("Export All Queue Counts")]
        public string ExportAllQueueCounts()
        {
            string folder = Path.Combine(Application.persistentDataPath, exportFolder);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_queue_counts.csv");

            var builder = new StringBuilder();
            builder.AppendLine("queue,count");
            builder.AppendLine("cameraTiming," + (cameraTimingQueue != null ? cameraTimingQueue.Count : 0));
            builder.AppendLine("cameraImage," + (cameraImageQueue != null ? cameraImageQueue.Count : 0));
            builder.AppendLine("cameraPose," + (cameraPoseQueue != null ? cameraPoseQueue.Count : 0));
            builder.AppendLine("cameraMetadata," + (cameraMetadataQueue != null ? cameraMetadataQueue.Count : 0));
            builder.AppendLine("cameraStreamState," + (cameraStreamStateQueue != null ? cameraStreamStateQueue.Count : 0));
            builder.AppendLine("controller," + (controllerQueue != null ? controllerQueue.Count : 0));
            builder.AppendLine("headset," + (headsetQueue != null ? headsetQueue.Count : 0));
            builder.AppendLine("network," + (networkQueue != null ? networkQueue.Count : 0));
            builder.AppendLine("virtualLayer," + (virtualLayerQueue != null ? virtualLayerQueue.Count : 0));

            File.WriteAllText(path, builder.ToString());
            Debug.Log("Queue snapshot CSV exported to: " + path, this);
            return path;
        }
    }
}
