using UnityEngine;

namespace DataCapture.Networking
{
    public enum EncodingPipelineMode
    {
        Disabled,
        DebugImageOnly,
        VideoOnly,
        DebugImageAndVideo
    }

    public enum VideoEncoderBackend
    {
        DebugJpeg,
        AndroidMediaCodecH264,
        AndroidMediaCodecH265,
        WebRtc
    }

    public enum CaptureVideoOutputMode
    {
        Disabled,
        DebugLowFpsImage,
        LocalMp4Save,
        RealtimeStreamSend
    }

    [CreateAssetMenu(fileName = "EncodingPipelineConfigurationSO", menuName = "DataCapture/50 Encoding Network/Encoding Pipeline Configuration")]
    public class EncodingPipelineConfigurationSO : ScriptableObject
    {
        [Header("Output Mode")]
        public CaptureVideoOutputMode outputMode = CaptureVideoOutputMode.DebugLowFpsImage;

        [Header("Pipeline")]
        public EncodingPipelineMode pipelineMode = EncodingPipelineMode.DebugImageOnly;
        public VideoEncoderBackend videoEncoderBackend = VideoEncoderBackend.AndroidMediaCodecH264;
        public bool allowDebugImageDuringVideo;

        public bool AllowsDebugImage =>
            outputMode == CaptureVideoOutputMode.DebugLowFpsImage &&
            (pipelineMode == EncodingPipelineMode.DebugImageOnly ||
                pipelineMode == EncodingPipelineMode.DebugImageAndVideo ||
                pipelineMode == EncodingPipelineMode.VideoOnly &&
                allowDebugImageDuringVideo);

        public bool AllowsVideo =>
            outputMode == CaptureVideoOutputMode.RealtimeStreamSend &&
            (pipelineMode == EncodingPipelineMode.VideoOnly ||
                pipelineMode == EncodingPipelineMode.DebugImageAndVideo);

        public bool AllowsLocalMp4Save => outputMode == CaptureVideoOutputMode.LocalMp4Save;
    }
}
