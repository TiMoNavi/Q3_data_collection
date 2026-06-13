using System;
using System.IO;
using Meta.XR;
using UnityEngine;
using UnityEngine.Rendering;

namespace DataCapture.Networking
{
    public sealed class PassthroughCameraTextureReadinessProbe : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private PassthroughCameraAccess cameraAccess;
        [SerializeField] private bool autoFindCameraAccess = true;

        [Header("Probe")]
        [SerializeField] private bool runOnEnable = true;
        [SerializeField] private bool keepProbingUntilReady = true;
        [SerializeField] private float probeIntervalSeconds = 0.5f;
        [SerializeField] private float autoToggleAfterSeconds = 3f;
        [SerializeField] private int maxAutoToggles = 0;
        [SerializeField] private int probeMaxDimension = 320;
        [SerializeField] private bool saveProbePng = true;
        [SerializeField] private string outputFolderName = "DataCapture/PassthroughCameraTextureProbe";
        [SerializeField] private string filePrefix = "pca_texture_probe";

        [Header("Validity Thresholds")]
        [SerializeField] private float minAverageLuma = 0.02f;
        [SerializeField] private float minLumaRange = 0.03f;
        [SerializeField] private float minChannelRange = 0.03f;

        [Header("Result")]
        [SerializeField] private bool isReady;
        [SerializeField] private bool isProbing;
        [SerializeField] private string lastStatus = "Idle.";
        [SerializeField] private string textureType;
        [SerializeField] private string nativeTexturePointerHex;
        [SerializeField] private Vector2Int sourceResolution;
        [SerializeField] private Vector2Int probeResolution;
        [SerializeField] private bool cameraIsSupported;
        [SerializeField] private bool cameraIsPlaying;
        [SerializeField] private bool cameraUpdatedThisFrame;
        [SerializeField] private float averageR;
        [SerializeField] private float averageG;
        [SerializeField] private float averageB;
        [SerializeField] private float averageLuma;
        [SerializeField] private float lumaRange;
        [SerializeField] private float channelRange;
        [SerializeField] private int successfulProbeCount;
        [SerializeField] private int failedProbeCount;
        [SerializeField] private int autoToggleCount;
        [SerializeField] private string lastProbePngPath;

        private RenderTexture probeRenderTexture;
        private float enabledAt;
        private float nextProbeAt;
        private bool readbackInFlight;

        public bool IsReady => isReady;
        public string LastStatus => lastStatus;
        public string LastProbePngPath => lastProbePngPath;
        public Vector2Int SourceResolution => sourceResolution;
        public Vector2Int ProbeResolution => probeResolution;
        public float AverageLuma => averageLuma;
        public float LumaRange => lumaRange;
        public float ChannelRange => channelRange;

        private void Awake()
        {
            ResolveCameraAccess();
        }

        private void OnEnable()
        {
            enabledAt = Time.unscaledTime;
            nextProbeAt = Time.unscaledTime;
            if (runOnEnable)
            {
                RequestProbe();
            }
        }

        private void Update()
        {
            if (isReady)
            {
                isProbing = false;
                return;
            }

            if (!runOnEnable && !isProbing)
            {
                return;
            }

            if (Time.unscaledTime < nextProbeAt)
            {
                return;
            }

            RequestProbe();
        }

        private void OnDisable()
        {
            ReleaseProbeRenderTexture();
        }

        [ContextMenu("Request PCA Texture Probe")]
        public void RequestProbe()
        {
            if (readbackInFlight)
            {
                return;
            }

            isProbing = true;
            nextProbeAt = Time.unscaledTime + Mathf.Max(0.1f, probeIntervalSeconds);
            ResolveCameraAccess();

            if (cameraAccess == null)
            {
                MarkProbeFailed("No PassthroughCameraAccess found.");
                return;
            }

            cameraIsSupported = PassthroughCameraAccess.IsSupported;
            cameraIsPlaying = cameraAccess.IsPlaying;
            cameraUpdatedThisFrame = cameraAccess.IsUpdatedThisFrame;

            if (!cameraAccess.enabled)
            {
                MarkProbeFailed("PassthroughCameraAccess is disabled.");
                return;
            }

            if (!cameraAccess.IsPlaying)
            {
                TryAutoToggleIfStuck();
                MarkProbeFailed("Waiting for PassthroughCameraAccess.IsPlaying.");
                return;
            }

            Texture source = cameraAccess.GetTexture();
            if (source == null)
            {
                MarkProbeFailed("PassthroughCameraAccess.GetTexture() returned null.");
                return;
            }

            textureType = source.GetType().FullName;
            IntPtr nativePtr = source.GetNativeTexturePtr();
            nativeTexturePointerHex = nativePtr == IntPtr.Zero ? "0" : "0x" + nativePtr.ToInt64().ToString("x");
            sourceResolution = ResolveSourceResolution(source);
            probeResolution = ResolveProbeResolution(sourceResolution);
            EnsureProbeRenderTexture(probeResolution);

            if (probeRenderTexture == null)
            {
                MarkProbeFailed("Failed to create probe RenderTexture.");
                return;
            }

            Graphics.Blit(source, probeRenderTexture);
            readbackInFlight = true;
            lastStatus = "PCA texture probe readback requested.";
            AsyncGPUReadback.Request(probeRenderTexture, 0, TextureFormat.RGBA32, OnReadbackComplete);
        }

        [ContextMenu("Reset PCA Texture Probe")]
        public void ResetProbe()
        {
            isReady = false;
            isProbing = false;
            readbackInFlight = false;
            successfulProbeCount = 0;
            failedProbeCount = 0;
            autoToggleCount = 0;
            lastStatus = "Reset.";
            lastProbePngPath = string.Empty;
            averageR = 0f;
            averageG = 0f;
            averageB = 0f;
            averageLuma = 0f;
            lumaRange = 0f;
            channelRange = 0f;
            ReleaseProbeRenderTexture();
        }

        private void OnReadbackComplete(AsyncGPUReadbackRequest request)
        {
            readbackInFlight = false;

            if (request.hasError)
            {
                MarkProbeFailed("AsyncGPUReadback failed.");
                return;
            }

            var pixels = request.GetData<Color32>();
            if (pixels.Length == 0)
            {
                MarkProbeFailed("Probe readback returned no pixels.");
                return;
            }

            Color32[] pngPixels = saveProbePng ? pixels.ToArray() : null;
            AnalyzePixels(pixels);

            bool valid = averageLuma >= minAverageLuma &&
                lumaRange >= minLumaRange &&
                channelRange >= minChannelRange;

            if (saveProbePng && pngPixels != null)
            {
                lastProbePngPath = SaveProbePng(pngPixels);
            }

            successfulProbeCount++;
            isReady = valid;
            isProbing = keepProbingUntilReady && !isReady;
            lastStatus = valid
                ? "PCA texture is ready. png=" + lastProbePngPath
                : "PCA texture readback was weak/flat. avgLuma=" + averageLuma.ToString("0.000") +
                  " lumaRange=" + lumaRange.ToString("0.000") +
                  " channelRange=" + channelRange.ToString("0.000");

            if (valid)
            {
                Debug.Log(lastStatus, this);
            }
            else
            {
                Debug.LogWarning(lastStatus, this);
            }
        }

        private void AnalyzePixels(Unity.Collections.NativeArray<Color32> pixels)
        {
            double sumR = 0;
            double sumG = 0;
            double sumB = 0;
            float minLuma = 1f;
            float maxLuma = 0f;
            byte minChannel = 255;
            byte maxChannel = 0;
            int stride = Mathf.Max(1, pixels.Length / 4096);
            int sampleCount = 0;

            for (int i = 0; i < pixels.Length; i += stride)
            {
                Color32 pixel = pixels[i];
                sumR += pixel.r;
                sumG += pixel.g;
                sumB += pixel.b;

                float luma = (0.2126f * pixel.r + 0.7152f * pixel.g + 0.0722f * pixel.b) / 255f;
                minLuma = Mathf.Min(minLuma, luma);
                maxLuma = Mathf.Max(maxLuma, luma);
                minChannel = Math.Min(minChannel, Math.Min(pixel.r, Math.Min(pixel.g, pixel.b)));
                maxChannel = Math.Max(maxChannel, Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)));
                sampleCount++;
            }

            double inv = sampleCount > 0 ? 1.0 / sampleCount : 0.0;
            averageR = (float)(sumR * inv / 255.0);
            averageG = (float)(sumG * inv / 255.0);
            averageB = (float)(sumB * inv / 255.0);
            averageLuma = 0.2126f * averageR + 0.7152f * averageG + 0.0722f * averageB;
            lumaRange = Mathf.Max(0f, maxLuma - minLuma);
            channelRange = (maxChannel - minChannel) / 255f;
        }

        private string SaveProbePng(Color32[] pixels)
        {
            string directory = Path.Combine(Application.persistentDataPath, outputFolderName);
            Directory.CreateDirectory(directory);

            string path = Path.Combine(
                directory,
                filePrefix + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".png");

            var texture = new Texture2D(probeResolution.x, probeResolution.y, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                return path;
            }
            finally
            {
                DestroyUnityObject(texture);
            }
        }

        private void TryAutoToggleIfStuck()
        {
            if (maxAutoToggles <= 0 || autoToggleCount >= maxAutoToggles)
            {
                return;
            }

            if (Time.unscaledTime - enabledAt < Mathf.Max(0f, autoToggleAfterSeconds))
            {
                return;
            }

            autoToggleCount++;
            cameraAccess.enabled = false;
            cameraAccess.enabled = true;
            lastStatus = "Toggled PassthroughCameraAccess once to recover Play Mode PCA.";
            Debug.LogWarning(lastStatus, this);
        }

        private void MarkProbeFailed(string reason)
        {
            failedProbeCount++;
            isReady = false;
            isProbing = keepProbingUntilReady;
            lastStatus = reason;
        }

        private void ResolveCameraAccess()
        {
            if (cameraAccess == null && autoFindCameraAccess)
            {
                cameraAccess = FindAnyObjectByType<PassthroughCameraAccess>();
            }
        }

        private Vector2Int ResolveSourceResolution(Texture source)
        {
            Vector2Int resolution = cameraAccess != null ? cameraAccess.CurrentResolution : default;
            if (resolution.x <= 0 || resolution.y <= 0)
            {
                resolution = new Vector2Int(source.width, source.height);
            }

            return new Vector2Int(Mathf.Max(1, resolution.x), Mathf.Max(1, resolution.y));
        }

        private Vector2Int ResolveProbeResolution(Vector2Int source)
        {
            int maxDimension = Mathf.Clamp(probeMaxDimension, 16, 2048);
            float scale = Mathf.Min(1f, maxDimension / (float)Mathf.Max(source.x, source.y));
            int width = Mathf.Max(1, Mathf.RoundToInt(source.x * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(source.y * scale));
            return new Vector2Int(width, height);
        }

        private void EnsureProbeRenderTexture(Vector2Int resolution)
        {
            if (probeRenderTexture != null &&
                probeRenderTexture.width == resolution.x &&
                probeRenderTexture.height == resolution.y)
            {
                return;
            }

            ReleaseProbeRenderTexture();
            probeRenderTexture = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.ARGB32)
            {
                name = "PCA Texture Readiness Probe RT",
                useMipMap = false,
                autoGenerateMips = false
            };
            probeRenderTexture.Create();
        }

        private void ReleaseProbeRenderTexture()
        {
            if (probeRenderTexture == null)
            {
                return;
            }

            probeRenderTexture.Release();
            DestroyUnityObject(probeRenderTexture);
            probeRenderTexture = null;
        }

        private static void DestroyUnityObject(UnityEngine.Object unityObject)
        {
            if (unityObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(unityObject);
            }
            else
            {
                DestroyImmediate(unityObject);
            }
        }
    }
}
