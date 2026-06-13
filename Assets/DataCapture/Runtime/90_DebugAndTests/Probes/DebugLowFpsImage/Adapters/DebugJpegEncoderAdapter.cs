using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Networking
{
    public class DebugJpegEncoderAdapter : MonoBehaviour, IVideoEncoderAdapter
    {
        [SerializeField] private EncoderConfigurationSO configuration;
        [SerializeField, Range(1, 100)] private int jpegQuality = 75;

        private long nextEncodedFrameId;

        public string CodecName => "JPEG";
        public bool IsReady => true;

        public bool TryEncode(Texture sourceTexture, CameraImageFrameRecord sourceFrame, out EncodedFrameRecord encodedFrame, out byte[] payload)
        {
            encodedFrame = default;
            payload = null;

            if (sourceTexture == null)
            {
                return false;
            }

            Texture2D texture2D = sourceTexture as Texture2D;
            if (texture2D == null)
            {
                return false;
            }

            payload = texture2D.EncodeToJPG(jpegQuality);
            encodedFrame = new EncodedFrameRecord
            {
                encodedFrameId = nextEncodedFrameId++,
                sourceCameraFrameId = sourceFrame.frameId,
                timestampUnixMs = sourceFrame.timestampUnixMs,
                isKeyFrame = true,
                width = texture2D.width,
                height = texture2D.height,
                codec = configuration != null ? configuration.codec : CodecName,
                byteLength = payload.Length,
                debugFilePath = string.Empty
            };

            return true;
        }
    }
}
