using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace DataCapture.Compositing
{
    /// <summary>
    /// Records PassthroughCameraLayerCompositor output as an image sequence for validation.
    /// Convert the exported sequence to MP4 on the host with the generated ffmpeg command.
    /// </summary>
    public sealed class PassthroughCompositeSequenceRecorder : MonoBehaviour
    {
        private enum ImageFormat
        {
            Jpg,
            Png
        }

        [SerializeField] private PassthroughCameraLayerCompositor compositor;
        [SerializeField] private bool autoStartOnEnable;
        [SerializeField] private float autoStopAfterSeconds;
        [SerializeField] private int maxFrames = 150;
        [SerializeField] private int frameStride = 1;
        [SerializeField] private int exportFps = 30;
        [SerializeField] private ImageFormat imageFormat = ImageFormat.Png;
        [Range(1, 100)]
        [SerializeField] private int jpgQuality = 85;
        [SerializeField] private string exportFolderName = "PassthroughCompositeExports";

        private bool isRecording;
        private int receivedFrameCount;
        private int writtenFrameCount;
        private int pendingReadbackCount;
        private bool stopRequested;
        private string sessionFolder;
        private string framesFolder;
        private StreamWriter metadataWriter;
        private Coroutine autoStopCoroutine;

        public bool IsRecording => isRecording;
        public string CurrentSessionFolder => sessionFolder;

        private void Awake()
        {
            if (compositor == null)
            {
                compositor = GetComponent<PassthroughCameraLayerCompositor>();
            }
        }

        private void OnEnable()
        {
            if (compositor != null)
            {
                compositor.FrameComposited += HandleFrameComposited;
            }

            if (autoStartOnEnable)
            {
                StartRecording();
            }
        }

        private void OnDisable()
        {
            if (compositor != null)
            {
                compositor.FrameComposited -= HandleFrameComposited;
            }

            StopRecording();
        }

        [ContextMenu("Start Composite Recording")]
        public void StartRecording()
        {
            if (isRecording)
            {
                return;
            }

            receivedFrameCount = 0;
            writtenFrameCount = 0;
            pendingReadbackCount = 0;
            stopRequested = false;
            var sessionName = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            sessionFolder = Path.Combine(Application.persistentDataPath, exportFolderName, sessionName);
            framesFolder = Path.Combine(sessionFolder, "frames");
            Directory.CreateDirectory(framesFolder);

            metadataWriter = new StreamWriter(Path.Combine(sessionFolder, "metadata.csv"));
            metadataWriter.WriteLine("index,unityFrame,timestampUnixMs,timestampIso,resolutionX,resolutionY,posX,posY,posZ,rotX,rotY,rotZ,rotW");

            WriteFfmpegCommandFile();
            isRecording = true;

            if (autoStopAfterSeconds > 0f)
            {
                autoStopCoroutine = StartCoroutine(AutoStopRoutine(autoStopAfterSeconds));
            }

            Debug.Log($"Composite recording started: {sessionFolder}");
        }

        [ContextMenu("Stop Composite Recording")]
        public void StopRecording()
        {
            if (!isRecording && metadataWriter == null)
            {
                return;
            }

            isRecording = false;
            stopRequested = true;

            if (autoStopCoroutine != null)
            {
                StopCoroutine(autoStopCoroutine);
                autoStopCoroutine = null;
            }

            if (pendingReadbackCount == 0)
            {
                FinishStop();
            }
            else
            {
                Debug.Log($"Composite recording stopping after {pendingReadbackCount} pending frame readbacks complete.");
            }
        }

        private void FinishStop()
        {
            metadataWriter?.Flush();
            metadataWriter?.Dispose();
            metadataWriter = null;

            Debug.Log($"Composite recording stopped. Wrote {writtenFrameCount} frames to: {sessionFolder}");
        }

        private IEnumerator AutoStopRoutine(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            StopRecording();
        }

        private void HandleFrameComposited(PassthroughCameraLayerCompositor.CompositeFrame frame)
        {
            if (!isRecording || frame.compositeTexture == null)
            {
                return;
            }

            receivedFrameCount++;
            if (frameStride > 1 && (receivedFrameCount - 1) % frameStride != 0)
            {
                return;
            }

            if (maxFrames > 0 && writtenFrameCount >= maxFrames)
            {
                StopRecording();
                return;
            }

            var frameIndex = writtenFrameCount;
            var timestamp = frame.timestamp;
            var timestampUnixMs = frame.timestampUnixMilliseconds;
            var pose = frame.cameraPose;
            var resolution = frame.resolution;
            var unityFrame = frame.unityFrame;
            var outputPath = Path.Combine(framesFolder, $"{frameIndex:D6}.{GetExtension()}");

            pendingReadbackCount++;
            AsyncGPUReadback.Request(frame.compositeTexture, 0, TextureFormat.RGBA32, request =>
            {
                try
                {
                    if (request.hasError)
                    {
                        Debug.LogWarning("Composite recording GPU readback failed.", this);
                        return;
                    }

                    var texture = new Texture2D(resolution.x, resolution.y, TextureFormat.RGBA32, false);
                    texture.SetPixelData(request.GetData<Color32>(), 0);
                    texture.Apply(false, false);

                    var bytes = imageFormat == ImageFormat.Png
                        ? texture.EncodeToPNG()
                        : texture.EncodeToJPG(jpgQuality);
                    Destroy(texture);

                    File.WriteAllBytes(outputPath, bytes);
                    metadataWriter?.WriteLine(
                        $"{frameIndex},{unityFrame},{timestampUnixMs},{timestamp:O},{resolution.x},{resolution.y}," +
                        $"{pose.position.x},{pose.position.y},{pose.position.z}," +
                        $"{pose.rotation.x},{pose.rotation.y},{pose.rotation.z},{pose.rotation.w}");
                    metadataWriter?.Flush();
                }
                finally
                {
                    pendingReadbackCount--;
                    if (stopRequested && pendingReadbackCount == 0)
                    {
                        FinishStop();
                    }
                }
            });

            writtenFrameCount++;
            if (maxFrames > 0 && writtenFrameCount >= maxFrames)
            {
                StopRecording();
            }
        }

        private string GetExtension()
        {
            return imageFormat == ImageFormat.Png ? "png" : "jpg";
        }

        private void WriteFfmpegCommandFile()
        {
            var extension = GetExtension();
            var command =
                $"ffmpeg -y -framerate {Mathf.Max(1, exportFps)} -i frames/%06d.{extension} " +
                "-c:v libx264 -crf 16 -preset slow -pix_fmt yuv420p composite_preview.mp4";
            File.WriteAllText(Path.Combine(sessionFolder, "make_video_ffmpeg.txt"), command + Environment.NewLine);
        }
    }
}
