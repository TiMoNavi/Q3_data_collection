using System.Collections.Generic;
using UnityEngine;

namespace DataCapture.Passthrough
{
    public class PassthroughStateCapture : MonoBehaviour
    {
        [SerializeField] private PassthroughStateData stateData;
        [SerializeField] private OVRPassthroughLayer passthroughLayer;
        [SerializeField] private OVRManager ovrManager;
        [SerializeField] private Camera centerEyeCamera;
        [SerializeField] private bool autoFindReferences = true;
        [SerializeField] private bool mirrorDiagnosticsToUnityConsole = true;
        [SerializeField] private float repeatedBlockerLogInterval = 5f;
        [SerializeField] private int maxRecentDiagnostics = 20;

        private bool layerResumed;
        private string lastBlockerSummary;
        private float nextRepeatedLogTime;

        private void Awake()
        {
            if (autoFindReferences)
            {
                AutoFindReferences();
            }
        }

        private void OnEnable()
        {
            SubscribeLayerEvent();
            CaptureNow("Passthrough state capture enabled.");
        }

        private void OnDisable()
        {
            UnsubscribeLayerEvent();
            if (stateData != null)
            {
                AddDiagnostic("Passthrough state capture disabled.", false);
            }
        }

        private void Update()
        {
            CaptureNow();
        }

        [ContextMenu("Auto Find References")]
        public void AutoFindReferences()
        {
            if (passthroughLayer == null)
            {
                passthroughLayer = FindAnyObjectByType<OVRPassthroughLayer>();
            }

            if (ovrManager == null)
            {
                ovrManager = FindAnyObjectByType<OVRManager>();
            }

            if (centerEyeCamera == null)
            {
                var centerEye = GameObject.Find("CenterEyeAnchor");
                if (centerEye != null)
                {
                    centerEyeCamera = centerEye.GetComponent<Camera>();
                }

                if (centerEyeCamera == null && Camera.main != null)
                {
                    centerEyeCamera = Camera.main;
                }
            }
        }

        [ContextMenu("Capture Now")]
        public void CaptureNow()
        {
            CaptureNow(null);
        }

        private void CaptureNow(string diagnosticMessage)
        {
            if (stateData == null)
            {
                return;
            }

            if (autoFindReferences && (passthroughLayer == null || ovrManager == null || centerEyeCamera == null))
            {
                AutoFindReferences();
                SubscribeLayerEvent();
            }

            var blockers = new List<string>();

            stateData.updatedAtTime = Time.time;
            stateData.updatedAtRealtime = Time.realtimeSinceStartupAsDouble;

            CaptureLayerState(blockers);
            CaptureOvrManagerState(blockers);
            CaptureVisibilityContext(blockers);

            stateData.SetBlockingReasons(blockers);
            stateData.isLikelyVisible = blockers.Count == 0;

            if (!string.IsNullOrEmpty(diagnosticMessage))
            {
                AddDiagnostic(diagnosticMessage, false);
            }

            if (lastBlockerSummary != stateData.blockerSummary)
            {
                lastBlockerSummary = stateData.blockerSummary;
                AddDiagnostic("Passthrough blockers: " + stateData.blockerSummary, !stateData.isLikelyVisible);
            }
            else if (!stateData.isLikelyVisible && Time.time >= nextRepeatedLogTime)
            {
                nextRepeatedLogTime = Time.time + repeatedBlockerLogInterval;
                AddDiagnostic("Still blocked: " + stateData.blockerSummary, true);
            }
        }

        private void CaptureLayerState(List<string> blockers)
        {
            stateData.layerFound = passthroughLayer != null;
            if (passthroughLayer == null)
            {
                stateData.layerObjectPath = string.Empty;
                stateData.layerGameObjectActive = false;
                stateData.layerComponentEnabled = false;
                stateData.layerHidden = false;
                stateData.layerResumed = false;
                stateData.textureOpacity = 0f;
                stateData.edgeRenderingEnabled = false;
                blockers.Add("OVRPassthroughLayer not found.");
                return;
            }

            stateData.layerObjectPath = GetPath(passthroughLayer.transform);
            stateData.layerGameObjectActive = passthroughLayer.gameObject.activeInHierarchy;
            stateData.layerComponentEnabled = passthroughLayer.enabled;
            stateData.layerHidden = passthroughLayer.hidden;
            stateData.layerResumed = layerResumed;
            stateData.textureOpacity = passthroughLayer.textureOpacity;
            stateData.edgeRenderingEnabled = passthroughLayer.edgeRenderingEnabled;

            if (!stateData.layerGameObjectActive)
            {
                blockers.Add("OVRPassthroughLayer GameObject is inactive.");
            }

            if (!stateData.layerComponentEnabled)
            {
                blockers.Add("OVRPassthroughLayer component is disabled.");
            }

            if (stateData.layerHidden)
            {
                blockers.Add("OVRPassthroughLayer.hidden is true.");
            }

            if (stateData.textureOpacity <= 0.001f)
            {
                blockers.Add("OVRPassthroughLayer.textureOpacity is zero.");
            }

            if (!stateData.layerResumed && Application.isPlaying)
            {
                blockers.Add("Waiting for OVRPassthroughLayer.passthroughLayerResumed.");
            }
        }

        private void CaptureOvrManagerState(List<string> blockers)
        {
            stateData.ovrManagerFound = ovrManager != null;
            if (ovrManager == null)
            {
                stateData.ovrManagerObjectPath = string.Empty;
                stateData.isInsightPassthroughEnabled = false;
                stateData.isSupportedPlatform = false;
                stateData.isUserPresent = false;
                stateData.isPassthroughRecommended = false;
                stateData.enableMixedReality = false;
                blockers.Add("OVRManager not found.");
                return;
            }

            stateData.ovrManagerObjectPath = GetPath(ovrManager.transform);
            stateData.isInsightPassthroughEnabled = ovrManager.isInsightPassthroughEnabled;
            stateData.isSupportedPlatform = ovrManager.isSupportedPlatform;
            stateData.isUserPresent = ovrManager.isUserPresent;
            stateData.enableMixedReality = ovrManager.enableMixedReality;

            try
            {
                stateData.isPassthroughRecommended = OVRManager.IsPassthroughRecommended();
            }
            catch
            {
                stateData.isPassthroughRecommended = false;
            }

            if (!stateData.isInsightPassthroughEnabled)
            {
                blockers.Add("OVRManager.isInsightPassthroughEnabled is false.");
            }

            if (Application.isPlaying && !stateData.isSupportedPlatform)
            {
                blockers.Add("OVRManager.isSupportedPlatform is false at runtime.");
            }

            if (Application.isPlaying && !stateData.isUserPresent)
            {
                blockers.Add("OVRManager.isUserPresent is false.");
            }
        }

        private void CaptureVisibilityContext(List<string> blockers)
        {
            stateData.centerEyeCameraFound = centerEyeCamera != null;
            if (centerEyeCamera == null)
            {
                stateData.centerEyeCameraPath = string.Empty;
                stateData.centerEyeClearFlags = CameraClearFlags.Skybox;
                stateData.centerEyeBackgroundColor = Color.black;
                stateData.skyboxMaterialAssigned = RenderSettings.skybox != null;
                blockers.Add("Center eye Camera not found.");
                return;
            }

            stateData.centerEyeCameraPath = GetPath(centerEyeCamera.transform);
            stateData.centerEyeClearFlags = centerEyeCamera.clearFlags;
            stateData.centerEyeBackgroundColor = centerEyeCamera.backgroundColor;
            stateData.skyboxMaterialAssigned = RenderSettings.skybox != null;

            if (stateData.skyboxMaterialAssigned && centerEyeCamera.clearFlags == CameraClearFlags.Skybox)
            {
                blockers.Add("Skybox is assigned and CenterEye camera uses Skybox clear flags; passthrough may be hidden behind skybox.");
            }

            if (centerEyeCamera.clearFlags == CameraClearFlags.SolidColor && centerEyeCamera.backgroundColor.a > 0.001f)
            {
                blockers.Add("CenterEye camera background alpha is opaque; passthrough underlay may be hidden.");
            }
        }

        private void SubscribeLayerEvent()
        {
            if (passthroughLayer != null)
            {
                passthroughLayer.passthroughLayerResumed.RemoveListener(OnPassthroughLayerResumed);
                passthroughLayer.passthroughLayerResumed.AddListener(OnPassthroughLayerResumed);
            }
        }

        private void UnsubscribeLayerEvent()
        {
            if (passthroughLayer != null)
            {
                passthroughLayer.passthroughLayerResumed.RemoveListener(OnPassthroughLayerResumed);
            }
        }

        private void OnPassthroughLayerResumed(OVRPassthroughLayer layer)
        {
            layerResumed = true;
            if (stateData != null)
            {
                AddDiagnostic("OVRPassthroughLayer resumed.", false);
            }
        }

        private static string GetPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }

        private string FormatLog(string message)
        {
            return $"[{System.DateTime.Now:HH:mm:ss}] {message}";
        }

        private void AddDiagnostic(string message, bool warning)
        {
            string formatted = FormatLog(message);
            stateData.AddDiagnostic(formatted, maxRecentDiagnostics);

            if (!mirrorDiagnosticsToUnityConsole)
            {
                return;
            }

            if (warning)
            {
                Debug.LogWarning(formatted, this);
            }
            else
            {
                Debug.Log(formatted, this);
            }
        }
    }
}
