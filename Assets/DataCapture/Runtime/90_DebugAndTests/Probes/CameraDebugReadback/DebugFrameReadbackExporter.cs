using System;
using System.IO;
using UnityEngine;

namespace DataCapture.DebugReadback
{
    public class DebugFrameReadbackExporter : MonoBehaviour
    {
        [SerializeField] private string exportFolder = "DataCaptureDebug/Frames";

        public string ExportTexture2D(Texture2D texture, string filePrefix = "frame")
        {
            if (texture == null)
            {
                return string.Empty;
            }

            string folder = Path.Combine(Application.persistentDataPath, exportFolder);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, filePrefix + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".jpg");
            File.WriteAllBytes(path, texture.EncodeToJPG(80));
            return path;
        }
    }
}
