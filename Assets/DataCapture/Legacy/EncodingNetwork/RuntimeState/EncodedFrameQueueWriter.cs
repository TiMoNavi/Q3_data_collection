using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Networking
{
    public class EncodedFrameQueueWriter : MonoBehaviour
    {
        [SerializeField] private CurrentCameraImageSO currentCameraImage;
        [SerializeField] private CurrentEncodedFrameSO currentEncodedFrame;
        [SerializeField] private MonoBehaviour encoderAdapterBehaviour;

        private IVideoEncoderAdapter Encoder => encoderAdapterBehaviour as IVideoEncoderAdapter;

        [ContextMenu("Encode Current Camera Frame")]
        public bool EncodeCurrentCameraFrame()
        {
            if (currentCameraImage == null || !currentCameraImage.isValid || currentEncodedFrame == null)
            {
                return false;
            }

            IVideoEncoderAdapter encoder = Encoder;
            if (encoder == null || !encoder.IsReady)
            {
                return false;
            }

            bool encoded = encoder.TryEncode(
                currentCameraImage.currentTexture,
                currentCameraImage.ToRecord(),
                out EncodedFrameRecord encodedFrame,
                out _);

            if (!encoded)
            {
                return false;
            }

            currentEncodedFrame.SetFrame(encodedFrame);
            return true;
        }
    }
}
