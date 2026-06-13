using UnityEngine;

namespace DataCapture.DebugReadback
{
    public class LowFpsImageSequenceExporter : MonoBehaviour
    {
        [SerializeField] private DebugFrameReadbackExporter exporter;
        [SerializeField] private float exportIntervalSeconds = 1f;

        private float nextExportTime;
        private long frameIndex;

        public bool TryExport(Texture2D texture)
        {
            if (Time.unscaledTime < nextExportTime || exporter == null || texture == null)
            {
                return false;
            }

            nextExportTime = Time.unscaledTime + Mathf.Max(0.01f, exportIntervalSeconds);
            exporter.ExportTexture2D(texture, "debug_" + frameIndex.ToString("D6"));
            frameIndex++;
            return true;
        }
    }
}
