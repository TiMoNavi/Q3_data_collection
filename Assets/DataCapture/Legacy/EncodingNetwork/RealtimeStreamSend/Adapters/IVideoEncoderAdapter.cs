using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Networking
{
    public interface IVideoEncoderAdapter
    {
        string CodecName { get; }
        bool IsReady { get; }
        bool TryEncode(Texture sourceTexture, CameraImageFrameRecord sourceFrame, out EncodedFrameRecord encodedFrame, out byte[] payload);
    }
}
