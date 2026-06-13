using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DataCapture.Networking
{
    public sealed class SingleEncodePcSmokeRunner : MonoBehaviour
    {
        [Header("Run")]
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private float startDelaySeconds = 1f;
        [SerializeField] private string ffmpegExecutable = "ffmpeg";

        [Header("Synthetic Source")]
        [SerializeField] private int width = 320;
        [SerializeField] private int height = 320;
        [SerializeField] private int frameRate = 30;
        [SerializeField] private int frameCount = 60;
        [SerializeField] private int bitrateKbps = 2000;

        [Header("Output Queue")]
        [SerializeField] private CurrentCaptureOutputSO currentOutput;
        [SerializeField] private CaptureOutputQueueSO outputQueue;
        [SerializeField] private string outputFolderName = "DataCapture/SingleEncodePcSmoke";

        [Header("Result")]
        [SerializeField] private bool isRunning;
        [SerializeField] private bool lastRunSucceeded;
        [SerializeField] private string lastStatus;
        [SerializeField] private string h264Path;
        [SerializeField] private string mp4Path;
        [SerializeField] private string manifestPath;
        [SerializeField] private int publishedFramePacketCount;
        [SerializeField] private long h264Bytes;
        [SerializeField] private long mp4Bytes;

        private long nextOutputId;

        private async void Start()
        {
            if (!runOnStart)
            {
                return;
            }

            int delayMs = Mathf.RoundToInt(Mathf.Max(0f, startDelaySeconds) * 1000f);
            if (delayMs > 0)
            {
                await Task.Delay(delayMs);
            }

            await RunSmokeTestAsync();
        }

        [ContextMenu("Run Single Encode PC Smoke Test")]
        public async void RunFromContextMenu()
        {
            await RunSmokeTestAsync();
        }

        public async Task<bool> RunSmokeTestAsync()
        {
            if (isRunning)
            {
                return lastRunSucceeded;
            }

            isRunning = true;
            lastRunSucceeded = false;
            lastStatus = "Starting PC single-encode smoke test.";
            publishedFramePacketCount = 0;
            h264Bytes = 0;
            mp4Bytes = 0;

            try
            {
                string sessionFolder = CreateSessionFolder();
                h264Path = Path.Combine(sessionFolder, "stream.h264");
                mp4Path = Path.Combine(sessionFolder, "capture.mp4");
                manifestPath = Path.Combine(sessionFolder, "manifest.json");

                int safeWidth = MakeEven(Mathf.Max(16, width));
                int safeHeight = MakeEven(Mathf.Max(16, height));
                int safeFrameRate = Mathf.Clamp(frameRate, 1, 120);
                int safeFrameCount = Mathf.Clamp(frameCount, 1, 600);
                int safeBitrate = Mathf.Max(128, bitrateKbps) * 1000;

                await Task.Run(() => EncodeSyntheticH264(safeWidth, safeHeight, safeFrameRate, safeFrameCount, safeBitrate));
                await Task.Run(() => RemuxH264ToMp4(safeFrameRate));

                h264Bytes = File.Exists(h264Path) ? new FileInfo(h264Path).Length : 0;
                mp4Bytes = File.Exists(mp4Path) ? new FileInfo(mp4Path).Length : 0;

                PublishStreamPackets(safeWidth, safeHeight, safeFrameRate);
                PublishMp4Artifact(safeWidth, safeHeight, safeFrameRate, safeFrameCount);
                WriteManifest(safeWidth, safeHeight, safeFrameRate, safeFrameCount);

                lastRunSucceeded = h264Bytes > 0 && mp4Bytes > 0 && publishedFramePacketCount > 0;
                lastStatus = lastRunSucceeded
                    ? "PC single-encode smoke test succeeded."
                    : "PC single-encode smoke test produced incomplete output.";
            }
            catch (Exception ex)
            {
                lastStatus = "PC single-encode smoke test failed: " + ex.Message;
                Debug.LogException(ex, this);
            }
            finally
            {
                isRunning = false;
            }

            string log = "SingleEncodePcSmokeRunner result: success=" + lastRunSucceeded +
                " h264Bytes=" + h264Bytes +
                " mp4Bytes=" + mp4Bytes +
                " packets=" + publishedFramePacketCount +
                " mp4Path=" + mp4Path +
                " status=" + lastStatus;
            if (lastRunSucceeded)
            {
                Debug.Log(log, this);
            }
            else
            {
                Debug.LogWarning(log, this);
            }

            return lastRunSucceeded;
        }

        private string CreateSessionFolder()
        {
            string folder = Path.Combine(
                Application.persistentDataPath,
                outputFolderName,
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private void EncodeSyntheticH264(
            int safeWidth,
            int safeHeight,
            int safeFrameRate,
            int safeFrameCount,
            int safeBitrate)
        {
            var process = CreateFfmpegProcess(
                "-y -hide_banner -loglevel error " +
                "-f rawvideo -pix_fmt rgba " +
                "-s " + safeWidth + "x" + safeHeight + " " +
                "-r " + safeFrameRate + " " +
                "-i pipe:0 " +
                "-frames:v " + safeFrameCount + " " +
                "-c:v libx264 -preset ultrafast -tune zerolatency " +
                "-b:v " + safeBitrate + " " +
                "-f h264 \"" + h264Path + "\"",
                redirectInput: true);

            RunProcess(process, stream => WriteSyntheticRgbaFrames(stream, safeWidth, safeHeight, safeFrameCount));
        }

        private void RemuxH264ToMp4(int safeFrameRate)
        {
            var process = CreateFfmpegProcess(
                "-y -hide_banner -loglevel error " +
                "-f h264 -r " + safeFrameRate + " " +
                "-i \"" + h264Path + "\" " +
                "-c:v copy \"" + mp4Path + "\"",
                redirectInput: false);

            RunProcess(process, null);
        }

        private Process CreateFfmpegProcess(string arguments, bool redirectInput)
        {
            var process = new Process();
            process.StartInfo.FileName = string.IsNullOrWhiteSpace(ffmpegExecutable)
                ? "ffmpeg"
                : ffmpegExecutable;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardInput = redirectInput;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            return process;
        }

        private static void RunProcess(Process process, Action<Stream> writeInput)
        {
            try
            {
                process.Start();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                if (writeInput != null)
                {
                    using (Stream stream = process.StandardInput.BaseStream)
                    {
                        writeInput(stream);
                    }
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

                    throw new TimeoutException("ffmpeg timed out.");
                }

                string error = errorTask.Result;
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("ffmpeg failed: " + error);
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        private void PublishStreamPackets(int safeWidth, int safeHeight, int safeFrameRate)
        {
            if (outputQueue == null || !File.Exists(h264Path))
            {
                return;
            }

            byte[] streamBytes = File.ReadAllBytes(h264Path);
            List<ArraySegment<byte>> nalUnits = SplitAnnexBNalUnits(streamBytes);
            long baseTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            for (int i = 0; i < nalUnits.Count; i++)
            {
                ArraySegment<byte> nal = nalUnits[i];
                byte[] payload = new byte[nal.Count];
                Buffer.BlockCopy(nal.Array, nal.Offset, payload, 0, nal.Count);

                var record = new CaptureOutputRecord
                {
                    outputId = nextOutputId++,
                    timestampUnixMs = baseTimestamp + i * 1000L / Mathf.Max(1, safeFrameRate),
                    outputKind = CaptureOutputKind.FramePacket,
                    deliveryKind = CaptureDeliveryKind.Stream,
                    payloadKind = CapturePayloadKind.H264AccessUnit,
                    payloadRef = CapturePayloadRef.FromBytes(payload),
                    metadataMode = CaptureMetadataMode.None,
                    status = CaptureOutputStatus.Ready,
                    sourceCameraFrameId = i,
                    sourceFrameStartId = i,
                    sourceFrameEndId = i,
                    codec = "H264",
                    width = safeWidth,
                    height = safeHeight,
                    frameRate = safeFrameRate,
                    isKeyFrame = i == 0,
                    byteLength = payload.Length
                };

                currentOutput?.SetRecord(record);
                outputQueue.RecordData(record);
                publishedFramePacketCount++;
            }
        }

        private void PublishMp4Artifact(int safeWidth, int safeHeight, int safeFrameRate, int safeFrameCount)
        {
            if (outputQueue == null || !File.Exists(mp4Path))
            {
                return;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long length = new FileInfo(mp4Path).Length;
            var record = new CaptureOutputRecord
            {
                outputId = nextOutputId++,
                timestampUnixMs = now,
                outputKind = CaptureOutputKind.FileArtifact,
                deliveryKind = CaptureDeliveryKind.OneShot,
                payloadKind = CapturePayloadKind.Mp4File,
                payloadRef = CapturePayloadRef.FromLocalFile(mp4Path, length),
                metadataMode = CaptureMetadataMode.SidecarFile,
                manifestPath = manifestPath,
                status = CaptureOutputStatus.Ready,
                sourceCameraFrameId = safeFrameCount - 1,
                sourceFrameStartId = 0,
                sourceFrameEndId = safeFrameCount - 1,
                timestampStartUnixMs = now - safeFrameCount * 1000L / Mathf.Max(1, safeFrameRate),
                timestampEndUnixMs = now,
                codec = "MP4",
                width = safeWidth,
                height = safeHeight,
                frameRate = safeFrameRate,
                byteLength = length > int.MaxValue ? int.MaxValue : (int)length
            };

            currentOutput?.SetRecord(record);
            outputQueue.RecordData(record);
        }

        private void WriteManifest(int safeWidth, int safeHeight, int safeFrameRate, int safeFrameCount)
        {
            var manifest = new SmokeManifest
            {
                h264Path = h264Path,
                mp4Path = mp4Path,
                width = safeWidth,
                height = safeHeight,
                frameRate = safeFrameRate,
                frameCount = safeFrameCount,
                h264Bytes = h264Bytes,
                mp4Bytes = mp4Bytes,
                publishedFramePacketCount = publishedFramePacketCount
            };
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
        }

        private static List<ArraySegment<byte>> SplitAnnexBNalUnits(byte[] bytes)
        {
            var result = new List<ArraySegment<byte>>();
            int start = FindStartCode(bytes, 0);
            if (start < 0)
            {
                result.Add(new ArraySegment<byte>(bytes));
                return result;
            }

            while (start >= 0)
            {
                int next = FindStartCode(bytes, start + StartCodeLength(bytes, start));
                if (next < 0)
                {
                    result.Add(new ArraySegment<byte>(bytes, start, bytes.Length - start));
                    break;
                }

                result.Add(new ArraySegment<byte>(bytes, start, next - start));
                start = next;
            }

            return result;
        }

        private static int FindStartCode(byte[] bytes, int offset)
        {
            for (int i = Mathf.Max(0, offset); i < bytes.Length - 3; i++)
            {
                if (bytes[i] == 0 && bytes[i + 1] == 0 && bytes[i + 2] == 1)
                {
                    return i;
                }

                if (i < bytes.Length - 4 && bytes[i] == 0 && bytes[i + 1] == 0 && bytes[i + 2] == 0 && bytes[i + 3] == 1)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int StartCodeLength(byte[] bytes, int offset)
        {
            return offset + 3 < bytes.Length && bytes[offset] == 0 && bytes[offset + 1] == 0 && bytes[offset + 2] == 0 && bytes[offset + 3] == 1
                ? 4
                : 3;
        }

        private static void WriteSyntheticRgbaFrames(Stream stream, int safeWidth, int safeHeight, int safeFrameCount)
        {
            byte[] frame = new byte[safeWidth * safeHeight * 4];
            for (int frameIndex = 0; frameIndex < safeFrameCount; frameIndex++)
            {
                float t = safeFrameCount <= 1 ? 0f : frameIndex / (float)(safeFrameCount - 1);
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

        private static int MakeEven(int value)
        {
            return value % 2 == 0 ? value : value - 1;
        }

        [Serializable]
        private sealed class SmokeManifest
        {
            public string h264Path;
            public string mp4Path;
            public int width;
            public int height;
            public int frameRate;
            public int frameCount;
            public long h264Bytes;
            public long mp4Bytes;
            public int publishedFramePacketCount;
        }
    }
}
