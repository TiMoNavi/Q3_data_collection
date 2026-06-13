using System.Collections;
using UnityEngine;

#pragma warning disable 0414 // Several serialized smoke settings are consumed only in Android player builds.
namespace DataCapture.Networking.MediaCodecSandbox
{
    public class SurfaceVideoEncoderSmokeRunner : MonoBehaviour
    {
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private float startDelaySeconds = 2f;
        [SerializeField] private string codec = "H264";
        [SerializeField] private int width = 640;
        [SerializeField] private int height = 640;
        [SerializeField] private int frameRate = 30;
        [SerializeField] private int bitrateKbps = 4000;
        [SerializeField] private float keyFrameIntervalSeconds = 1f;
        [SerializeField] private int frameCount = 30;

        [Header("Result")]
        [SerializeField] private bool isRunning;
        [SerializeField] private bool lastRunSucceeded;
        [SerializeField] private string lastStatus = string.Empty;
        [SerializeField] private string encoderName = string.Empty;
        [SerializeField] private int encodedOutputFrames;
        [SerializeField] private long encodedOutputBytes;

        private const string JavaClassName = "com.q3datacapture.mediacodec.Q3SurfaceVideoEncoder";
        private bool isScheduled;

#if UNITY_ANDROID && !UNITY_EDITOR
#if Q3DC_ENABLE_LEGACY_CODEC_BOOTSTRAP
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapAndroidSmokeTest()
        {
            SurfaceVideoEncoderSmokeRunner runner = FindFirstObjectByType<SurfaceVideoEncoderSmokeRunner>();
            if (runner == null)
            {
                GameObject runnerObject = new GameObject("MediaCodec_Surface_Smoke_Test_AutoBootstrap");
                runner = runnerObject.AddComponent<SurfaceVideoEncoderSmokeRunner>();
                DontDestroyOnLoad(runnerObject);
            }

            Debug.Log("SurfaceVideoEncoderSmokeRunner bootstrap: runner=" + runner.name +
                " active=" + runner.isActiveAndEnabled, runner);
            runner.RunFromBootstrap();
        }
#endif
#endif

        private void Awake()
        {
            Debug.Log("SurfaceVideoEncoderSmokeRunner awake: runOnStart=" + runOnStart +
                " codec=" + codec +
                " size=" + width + "x" + height +
                " fps=" + frameRate, this);
        }

        private void Start()
        {
            if (runOnStart)
            {
                ScheduleSmokeTest();
            }
        }

        private IEnumerator StartAfterDelay()
        {
            if (startDelaySeconds > 0f)
            {
                yield return new WaitForSeconds(startDelaySeconds);
            }

            yield return RunSmokeTest();
        }

        [ContextMenu("Run Surface Video Encoder Smoke Test")]
        public void RunFromContextMenu()
        {
            ScheduleSmokeTest();
        }

        public void RunFromBootstrap()
        {
            ScheduleSmokeTest();
        }

        private void ScheduleSmokeTest()
        {
            if (!isRunning && !isScheduled)
            {
                isScheduled = true;
                StartCoroutine(StartAfterDelay());
            }
        }

        private IEnumerator RunSmokeTest()
        {
            isRunning = true;
            isScheduled = false;
            lastRunSucceeded = false;
            lastStatus = "Starting surface video encoder smoke test.";
            encodedOutputFrames = 0;
            encodedOutputBytes = 0;

#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject encoder = null;
            bool started = false;
            try
            {
                encoder = new AndroidJavaObject(JavaClassName);
                started = encoder.Call<bool>(
                    "start",
                    codec,
                    Mathf.Max(16, width),
                    Mathf.Max(16, height),
                    Mathf.Clamp(frameRate, 1, 120),
                    Mathf.Max(128, bitrateKbps),
                    Mathf.Max(0.1f, keyFrameIntervalSeconds));
                encoderName = encoder.Call<string>("getCodecName") ?? string.Empty;
                lastStatus = encoder.Call<string>("getLastStatus") ?? string.Empty;
            }
            catch (System.Exception exception)
            {
                lastStatus = "Start exception: " + exception.Message;
            }

            if (!started)
            {
                Debug.LogWarning("SurfaceVideoEncoderSmokeRunner failed to start: " + lastStatus, this);
                encoder?.Dispose();
                isRunning = false;
                yield break;
            }

            int safeFrameRate = Mathf.Clamp(frameRate, 1, 120);
            int safeFrameCount = Mathf.Clamp(frameCount, 1, 600);
            long frameDurationUs = 1000000L / safeFrameRate;

            for (int i = 0; i < safeFrameCount; i++)
            {
                byte[] payload = encoder.Call<byte[]>(
                    "encodePatternFrame",
                    i * frameDurationUs,
                    i == 0);
                if (payload != null && payload.Length > 0)
                {
                    encodedOutputFrames++;
                    encodedOutputBytes += payload.Length;
                }

                yield return null;
            }

            lastStatus = encoder.Call<string>("getLastStatus") ?? string.Empty;
            encoder.Call("stop");
            encoder.Dispose();
            lastRunSucceeded = encodedOutputFrames > 0 && encodedOutputBytes > 0;
#else
            lastStatus = "Surface video encoder smoke test only runs on Android device builds.";
#endif

            isRunning = false;
            string result = "SurfaceVideoEncoderSmokeRunner result: success=" + lastRunSucceeded +
                " codec=" + codec +
                " encoder=" + encoderName +
                " frames=" + encodedOutputFrames +
                " bytes=" + encodedOutputBytes +
                " status=" + lastStatus;
            if (lastRunSucceeded)
            {
                Debug.Log(result, this);
            }
            else
            {
                Debug.LogWarning(result, this);
            }

            yield break;
        }
    }
}
#pragma warning restore 0414
