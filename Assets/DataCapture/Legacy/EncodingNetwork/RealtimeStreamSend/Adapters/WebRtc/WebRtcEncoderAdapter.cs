using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Networking
{
    public class WebRtcEncoderAdapter : MonoBehaviour, IVideoEncoderAdapter
    {
        public string CodecName => "WebRTC";
        public bool IsReady => false;

        public bool TryEncode(Texture sourceTexture, CameraImageFrameRecord sourceFrame, out EncodedFrameRecord encodedFrame, out byte[] payload)
        {
            encodedFrame = default;
            payload = null;
            return false;
        }
    }
}
