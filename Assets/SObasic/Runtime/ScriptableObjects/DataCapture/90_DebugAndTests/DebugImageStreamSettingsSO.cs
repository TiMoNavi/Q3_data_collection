using UnityEngine;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "DebugImageStreamSettingsSO", menuName = "DataCapture/50 Encoding Network/Debug Image Stream Settings")]
    public class DebugImageStreamSettingsSO : ScriptableObject
    {
        [Header("Debug Image")]
        public float maxFramesPerSecond = 2f;
        public int maxDimension = 320;
        [Range(1, 100)] public int jpegQuality = 70;
        public bool requireSendableMergedSnapshot = true;

        private void OnValidate()
        {
            maxFramesPerSecond = Mathf.Max(0.1f, maxFramesPerSecond);
            maxDimension = Mathf.Max(16, maxDimension);
            jpegQuality = Mathf.Clamp(jpegQuality, 1, 100);
        }
    }
}
