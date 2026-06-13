using UnityEngine;

namespace DataCapture.Networking
{
    public enum VideoFrameInputSourceKind
    {
        RawCameraImage,
        PassthroughUnityComposite
    }

    [CreateAssetMenu(fileName = "VideoFrameInputConfigurationSO", menuName = "DataCapture/50 Encoding Network/Video Frame Input Configuration")]
    public class VideoFrameInputConfigurationSO : ScriptableObject
    {
        [Header("Source")]
        public VideoFrameInputSourceKind sourceKind = VideoFrameInputSourceKind.RawCameraImage;
        public bool fallbackToRawCameraImage = true;

        [Header("Staging")]
        public bool useSharedStagingRenderTexture = true;
        public RenderTextureFormat stagingFormat = RenderTextureFormat.ARGB32;
    }
}
