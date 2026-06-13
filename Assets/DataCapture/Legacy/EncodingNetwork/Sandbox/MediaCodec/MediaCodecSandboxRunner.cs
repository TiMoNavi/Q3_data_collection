using System.Threading.Tasks;
using UnityEngine;

namespace DataCapture.Networking.MediaCodecSandbox
{
    public enum MediaCodecSandboxBackend
    {
        Auto,
        AndroidMediaCodec,
        FfmpegSoftwareH264
    }

    public class MediaCodecSandboxRunner : MonoBehaviour
    {
        [Header("Smoke Test Settings")]
        [SerializeField] private MediaCodecSandboxBackend backend = MediaCodecSandboxBackend.Auto;
        [SerializeField] private int width = 640;
        [SerializeField] private int height = 640;
        [SerializeField] private int bitrateKbps = 4000;
        [SerializeField] private int frameRate = 30;
        [SerializeField] private int frameCount = 60;
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private float runOnStartDelaySeconds = 1f;

        [Header("PC / Editor FFmpeg Backend")]
        [SerializeField] private string ffmpegExecutable = "ffmpeg";
        [SerializeField] private bool keepFfmpegOutputFile;

        [Header("Result")]
        [SerializeField] private bool isRunning;
        [SerializeField] private bool lastRunSucceeded;
        [SerializeField, TextArea(4, 12)] private string lastResultJson;

        public bool IsRunning => isRunning;
        public bool LastRunSucceeded => lastRunSucceeded;
        public string LastResultJson => lastResultJson;

        private async void Start()
        {
            if (!runOnStart)
            {
                return;
            }

            int delayMs = Mathf.RoundToInt(Mathf.Max(0f, runOnStartDelaySeconds) * 1000f);
            if (delayMs > 0)
            {
                await Task.Delay(delayMs);
            }

            await RunSmokeTestAsync();
        }

        [ContextMenu("Run MediaCodec Smoke Test")]
        public async void RunFromContextMenu()
        {
            await RunSmokeTestAsync();
        }

        public async Task<string> RunSmokeTestAsync()
        {
            if (isRunning)
            {
                return lastResultJson;
            }

            isRunning = true;
            lastRunSucceeded = false;
            lastResultJson = string.Empty;

            try
            {
                MediaCodecSandboxBackend resolvedBackend = ResolveBackend();
                int safeWidth = Mathf.Max(16, width);
                int safeHeight = Mathf.Max(16, height);
                int safeBitrate = Mathf.Max(128, bitrateKbps) * 1000;
                int safeFrameRate = Mathf.Clamp(frameRate, 1, 120);
                int safeFrameCount = Mathf.Clamp(frameCount, 1, 600);

                if (resolvedBackend == MediaCodecSandboxBackend.FfmpegSoftwareH264)
                {
                    lastResultJson = await Task.Run(() =>
                        RunFfmpegSoftwareH264Test(
                            safeWidth,
                            safeHeight,
                            safeBitrate,
                            safeFrameRate,
                            safeFrameCount));
                    lastRunSucceeded = lastResultJson.Contains("\"success\":true");
                    return lastResultJson;
                }

#if UNITY_ANDROID && !UNITY_EDITOR
                string json = await Task.Run(() =>
                {
                    AndroidJNI.AttachCurrentThread();
                    try
                    {
                        using (var testClass = new AndroidJavaClass("com.q3datacapture.mediacodec.Q3MediaCodecSmokeTest"))
                        {
                            return testClass.CallStatic<string>(
                                "runSolidColorH264Test",
                                safeWidth,
                                safeHeight,
                                safeBitrate,
                                safeFrameRate,
                                safeFrameCount);
                        }
                    }
                    finally
                    {
                        AndroidJNI.DetachCurrentThread();
                    }
                });

                lastResultJson = json;
                lastRunSucceeded = json.Contains("\"success\":true");
#else
                lastResultJson = "{\"success\":false,\"backend\":\"AndroidMediaCodec\",\"error\":\"Android MediaCodec only runs on Android device builds. Use Auto or FfmpegSoftwareH264 in Editor/PC.\"}";
#endif
            }
            catch (System.Exception ex)
            {
                lastResultJson = "{\"success\":false,\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
            finally
            {
                isRunning = false;
            }

            Debug.Log("MediaCodec sandbox result: " + lastResultJson, this);
            return lastResultJson;
        }

        [ContextMenu("Run PC FFmpeg Smoke Test Blocking")]
        public void RunFfmpegSmokeTestBlockingFromContextMenu()
        {
            RunFfmpegSmokeTestBlocking();
        }

        public string RunFfmpegSmokeTestBlocking()
        {
            if (isRunning)
            {
                return lastResultJson;
            }

            isRunning = true;
            lastRunSucceeded = false;
            lastResultJson = string.Empty;

            try
            {
                int safeWidth = Mathf.Max(16, width);
                int safeHeight = Mathf.Max(16, height);
                int safeBitrate = Mathf.Max(128, bitrateKbps) * 1000;
                int safeFrameRate = Mathf.Clamp(frameRate, 1, 120);
                int safeFrameCount = Mathf.Clamp(frameCount, 1, 600);
                lastResultJson = RunFfmpegSoftwareH264Test(
                    safeWidth,
                    safeHeight,
                    safeBitrate,
                    safeFrameRate,
                    safeFrameCount);
                lastRunSucceeded = lastResultJson.Contains("\"success\":true");
            }
            catch (System.Exception ex)
            {
                lastResultJson = "{\"success\":false,\"backend\":\"FfmpegSoftwareH264\",\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
            finally
            {
                isRunning = false;
            }

            Debug.Log("MediaCodec sandbox blocking ffmpeg result: " + lastResultJson, this);
            return lastResultJson;
        }

        private MediaCodecSandboxBackend ResolveBackend()
        {
            if (backend != MediaCodecSandboxBackend.Auto)
            {
                return backend;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            return MediaCodecSandboxBackend.AndroidMediaCodec;
#else
            return MediaCodecSandboxBackend.FfmpegSoftwareH264;
#endif
        }

        private string RunFfmpegSoftwareH264Test(
            int safeWidth,
            int safeHeight,
            int safeBitrate,
            int safeFrameRate,
            int safeFrameCount)
        {
            string outputPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "q3dc_ffmpeg_sandbox_" + System.Guid.NewGuid().ToString("N") + ".h264");

            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = string.IsNullOrWhiteSpace(ffmpegExecutable)
                ? "ffmpeg"
                : ffmpegExecutable;
            process.StartInfo.Arguments =
                "-y -hide_banner -loglevel error " +
                "-f rawvideo -pix_fmt rgba " +
                "-s " + safeWidth + "x" + safeHeight + " " +
                "-r " + safeFrameRate + " " +
                "-i pipe:0 " +
                "-frames:v " + safeFrameCount + " " +
                "-c:v libx264 -preset ultrafast -tune zerolatency " +
                "-b:v " + safeBitrate + " " +
                "-f h264 \"" + outputPath + "\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            long startTicks = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try
            {
                process.Start();
                string errorText = string.Empty;
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                using (System.IO.Stream stream = process.StandardInput.BaseStream)
                {
                    WriteSyntheticRgbaFrames(stream, safeWidth, safeHeight, safeFrameCount);
                }

                bool exited = process.WaitForExit(30000);
                if (!exited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }

                    return "{\"success\":false,\"backend\":\"FfmpegSoftwareH264\",\"error\":\"ffmpeg timed out.\"}";
                }

                errorText = errorTask.Result;
                long outputBytes = System.IO.File.Exists(outputPath)
                    ? new System.IO.FileInfo(outputPath).Length
                    : 0;
                bool success = process.ExitCode == 0 && outputBytes > 0;
                long elapsedMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startTicks;
                string retainedPath = keepFfmpegOutputFile ? outputPath.Replace("\\", "\\\\") : string.Empty;

                if (!keepFfmpegOutputFile && System.IO.File.Exists(outputPath))
                {
                    System.IO.File.Delete(outputPath);
                }

                return "{"
                    + "\"success\":" + (success ? "true" : "false")
                    + ",\"backend\":\"FfmpegSoftwareH264\""
                    + ",\"encoderName\":\"ffmpeg libx264\""
                    + ",\"codec\":\"video/avc\""
                    + ",\"width\":" + safeWidth
                    + ",\"height\":" + safeHeight
                    + ",\"frameRate\":" + safeFrameRate
                    + ",\"targetFrameCount\":" + safeFrameCount
                    + ",\"encodedFrameCount\":" + safeFrameCount
                    + ",\"outputBytes\":" + outputBytes
                    + ",\"elapsedMs\":" + elapsedMs
                    + ",\"outputPath\":\"" + retainedPath + "\""
                    + ",\"error\":\"" + EscapeJson(errorText) + "\""
                    + "}";
            }
            finally
            {
                process.Dispose();
            }
        }

        private static void WriteSyntheticRgbaFrames(
            System.IO.Stream stream,
            int width,
            int height,
            int frameCount)
        {
            byte[] frame = new byte[width * height * 4];
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                float t = frameCount <= 1 ? 0f : frameIndex / (float)(frameCount - 1);
                byte r = (byte)Mathf.RoundToInt(t * 255f);
                byte g = 64;
                byte b = (byte)Mathf.RoundToInt((1f - t) * 255f);

                for (int i = 0; i < frame.Length; i += 4)
                {
                    frame[i] = r;
                    frame[i + 1] = g;
                    frame[i + 2] = b;
                    frame[i + 3] = 255;
                }

                stream.Write(frame, 0, frame.Length);
            }
        }

        private static string EscapeJson(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
