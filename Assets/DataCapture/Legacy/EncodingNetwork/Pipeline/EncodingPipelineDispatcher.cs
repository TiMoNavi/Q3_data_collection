using UnityEngine;

namespace DataCapture.Networking
{
    public class EncodingPipelineDispatcher : MonoBehaviour
    {
        [SerializeField] private EncodingPipelineConfigurationSO pipelineConfiguration;
        [SerializeField] private AsyncDebugJpegNetworkStreamer debugImageStreamer;
        [SerializeField] private AsyncMjpegVideoStreamEncoder mjpegVideoStreamer;
        [SerializeField] private VideoStreamEncoderRunner videoStreamEncoderRunner;
        [SerializeField] private bool dispatchOnUpdate = true;

        [Header("Runtime Diagnostics")]
        [SerializeField] private string lastDispatchStatus;
        [SerializeField] private int debugImageDispatchCount;
        [SerializeField] private int videoDispatchCount;
        [SerializeField] private int blockedDispatchCount;

        private void Update()
        {
            if (dispatchOnUpdate)
            {
                DispatchOnce();
            }
        }

        [ContextMenu("Dispatch Encoding Once")]
        public bool DispatchOnce()
        {
            if (pipelineConfiguration == null)
            {
                return Block("EncodingPipelineConfigurationSO is not assigned.");
            }

            if (pipelineConfiguration.pipelineMode == EncodingPipelineMode.Disabled)
            {
                return Block("Encoding pipeline is disabled.");
            }

            bool dispatchedAny = false;

            if (pipelineConfiguration.AllowsDebugImage && debugImageStreamer != null)
            {
                if (debugImageStreamer.TryQueueFrame())
                {
                    debugImageDispatchCount++;
                    dispatchedAny = true;
                }
            }

            if (pipelineConfiguration.AllowsVideo)
            {
                bool videoDispatched = false;
                if (pipelineConfiguration.videoEncoderBackend == VideoEncoderBackend.DebugJpeg)
                {
                    videoDispatched = mjpegVideoStreamer != null && mjpegVideoStreamer.TryQueueFrame();
                }
                else
                {
                    videoDispatched = videoStreamEncoderRunner != null && videoStreamEncoderRunner.TryEncodeLatestFrame();
                }

                if (videoDispatched)
                {
                    videoDispatchCount++;
                    dispatchedAny = true;
                }
            }

            if (!dispatchedAny)
            {
                return Block("No encoding output was dispatched this frame.");
            }

            lastDispatchStatus = "Dispatched " + pipelineConfiguration.pipelineMode + ".";
            return true;
        }

        private bool Block(string reason)
        {
            blockedDispatchCount++;
            lastDispatchStatus = reason;
            return false;
        }
    }
}
