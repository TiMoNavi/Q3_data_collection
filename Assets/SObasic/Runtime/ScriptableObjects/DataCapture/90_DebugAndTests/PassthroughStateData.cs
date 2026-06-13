using System.Collections.Generic;
using UnityEngine;

namespace DataCapture.Passthrough
{
    [CreateAssetMenu(fileName = "PassthroughStateData", menuName = "DataCapture/Passthrough/State Data")]
    public class PassthroughStateData : ScriptableObject
    {
        [Header("Summary")]
        public bool isLikelyVisible;
        public string blockerSummary;
        public float updatedAtTime;
        public double updatedAtRealtime;

        [Header("Layer")]
        public bool layerFound;
        public string layerObjectPath;
        public bool layerGameObjectActive;
        public bool layerComponentEnabled;
        public bool layerHidden;
        public bool layerResumed;
        public float textureOpacity;
        public bool edgeRenderingEnabled;

        [Header("OVR Manager")]
        public bool ovrManagerFound;
        public string ovrManagerObjectPath;
        public bool isInsightPassthroughEnabled;
        public bool isSupportedPlatform;
        public bool isUserPresent;
        public bool isPassthroughRecommended;
        public bool enableMixedReality;

        [Header("Visibility Context")]
        public bool centerEyeCameraFound;
        public string centerEyeCameraPath;
        public CameraClearFlags centerEyeClearFlags;
        public Color centerEyeBackgroundColor;
        public bool skyboxMaterialAssigned;

        [Header("Diagnostics")]
        public List<string> blockingReasons = new List<string>();
        public List<string> recentDiagnostics = new List<string>();

        public void ClearRuntimeState()
        {
            isLikelyVisible = false;
            blockerSummary = string.Empty;
            updatedAtTime = 0f;
            updatedAtRealtime = 0d;
            layerFound = false;
            layerObjectPath = string.Empty;
            layerGameObjectActive = false;
            layerComponentEnabled = false;
            layerHidden = false;
            layerResumed = false;
            textureOpacity = 0f;
            edgeRenderingEnabled = false;
            ovrManagerFound = false;
            ovrManagerObjectPath = string.Empty;
            isInsightPassthroughEnabled = false;
            isSupportedPlatform = false;
            isUserPresent = false;
            isPassthroughRecommended = false;
            enableMixedReality = false;
            centerEyeCameraFound = false;
            centerEyeCameraPath = string.Empty;
            centerEyeClearFlags = CameraClearFlags.Skybox;
            centerEyeBackgroundColor = Color.black;
            skyboxMaterialAssigned = false;
            blockingReasons.Clear();
            recentDiagnostics.Clear();
        }

        public void SetBlockingReasons(List<string> reasons)
        {
            blockingReasons.Clear();
            blockingReasons.AddRange(reasons);
            blockerSummary = blockingReasons.Count == 0 ? "None" : string.Join("; ", blockingReasons);
        }

        public void AddDiagnostic(string message, int maxEntries)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            recentDiagnostics.Insert(0, message);
            while (recentDiagnostics.Count > maxEntries)
            {
                recentDiagnostics.RemoveAt(recentDiagnostics.Count - 1);
            }
        }
    }
}
