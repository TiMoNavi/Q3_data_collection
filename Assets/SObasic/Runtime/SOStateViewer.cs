using UnityEngine;
using System;
using System.Collections.Generic;
using DataCapture.Diagnostics;
using DataCapture.Passthrough;
using DataCapture.Networking;
using DataCapture.Synchronization;
using DataCapture.Testing;
using UnityEngine.Serialization;

namespace DataCapture
{
    public class SOStateViewer : MonoBehaviour
    {
        [Header("Discovery")]
        public bool autoDiscoverInEditor = true;
        public List<string> searchFolders = new List<string>
        {
            "Assets/SOData",
            "Assets/DataCapture",
            "Assets/SObasic/Runtime/ScriptableObjects/DataCapture"
        };

        [Header("Display")]
        public bool showAdvancedDetails;

        [Header("DataCapture 00 Session Control")]
        [FormerlySerializedAs("dataCapture00Global")]
        public List<ScriptableObject> dataCapture00SessionControl = new List<ScriptableObject>();

        [Header("DataCapture 10 Current SO Inputs")]
        [FormerlySerializedAs("dataCapture10CameraCapture")]
        public List<ScriptableObject> dataCapture10CurrentSOInputs = new List<ScriptableObject>();

        [Header("DataCapture 20 Queue Buffers")]
        [FormerlySerializedAs("dataCapture20VirtualLayerCapture")]
        public List<ScriptableObject> dataCapture20QueueBuffers = new List<ScriptableObject>();

        [Header("DataCapture 30 Time Synchronization")]
        [FormerlySerializedAs("dataCapture40MergedSynchronization")]
        public List<ScriptableObject> dataCapture30TimeSynchronization = new List<ScriptableObject>();

        [Header("DataCapture 40 Single Encode Production")]
        [FormerlySerializedAs("dataCapture50EncodingNetwork")]
        public List<ScriptableObject> dataCapture40SingleEncodeProduction = new List<ScriptableObject>();

        [Header("DataCapture 50 Product Assembly")]
        public List<ScriptableObject> dataCapture50ProductAssembly = new List<ScriptableObject>();

        [Header("DataCapture 60 Distribution")]
        public List<ScriptableObject> dataCapture60Distribution = new List<ScriptableObject>();

        [Header("DataCapture 90 Debug And Tests")]
        [FormerlySerializedAs("dataCapture90Diagnostics")]
        public List<ScriptableObject> dataCapture90DebugAndTests = new List<ScriptableObject>();

        [Header("Passthrough Diagnostics")]
        public List<PassthroughStateData> passthroughStates = new List<PassthroughStateData>();

        [Header("Other ScriptableObjects")]
        public List<ScriptableObject> otherSOs = new List<ScriptableObject>();

        public void CopyAllScriptableObjects(List<ScriptableObject> results)
        {
            if (results == null)
            {
                return;
            }

            AddAll(results, dataCapture00SessionControl);
            AddAll(results, dataCapture10CurrentSOInputs);
            AddAll(results, dataCapture20QueueBuffers);
            AddAll(results, dataCapture30TimeSynchronization);
            AddAll(results, dataCapture40SingleEncodeProduction);
            AddAll(results, dataCapture50ProductAssembly);
            AddAll(results, dataCapture60Distribution);
            AddAll(results, dataCapture90DebugAndTests);
            AddAll(results, passthroughStates);
            AddAll(results, otherSOs);
        }

#if UNITY_EDITOR
        [ContextMenu("Auto Discover All SOs")]
        public void AutoDiscoverSOs()
        {
            dataCapture00SessionControl.Clear();
            dataCapture10CurrentSOInputs.Clear();
            dataCapture20QueueBuffers.Clear();
            dataCapture30TimeSynchronization.Clear();
            dataCapture40SingleEncodeProduction.Clear();
            dataCapture50ProductAssembly.Clear();
            dataCapture60Distribution.Clear();
            dataCapture90DebugAndTests.Clear();
            passthroughStates.Clear();
            otherSOs.Clear();

            var allSOs = UnityEditor.AssetDatabase.FindAssets(
                "t:ScriptableObject",
                GetValidSearchFolders());

            var discoveredAssets = new List<DiscoveredScriptableObject>();

            foreach (var guid in allSOs)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (IsLegacyAssetPath(path))
                {
                    continue;
                }

                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

                if (asset == null)
                {
                    continue;
                }

                discoveredAssets.Add(new DiscoveredScriptableObject(path, asset));
            }

            discoveredAssets.Sort((left, right) =>
                string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));

            foreach (var discovered in discoveredAssets)
            {
                var path = discovered.Path;
                var asset = discovered.Asset;

                if (asset is PassthroughStateData passthroughState) passthroughStates.Add(passthroughState);
                else if (IsDataCapture00SessionControl(path, asset)) dataCapture00SessionControl.Add(asset);
                else if (IsDataCapture10CurrentSOInputs(path, asset)) dataCapture10CurrentSOInputs.Add(asset);
                else if (IsDataCapture20QueueBuffers(path, asset)) dataCapture20QueueBuffers.Add(asset);
                else if (IsDataCapture30TimeSynchronization(path, asset)) dataCapture30TimeSynchronization.Add(asset);
                else if (IsDataCapture40SingleEncodeProduction(path, asset)) dataCapture40SingleEncodeProduction.Add(asset);
                else if (IsDataCapture50ProductAssembly(path, asset)) dataCapture50ProductAssembly.Add(asset);
                else if (IsDataCapture60Distribution(path, asset)) dataCapture60Distribution.Add(asset);
                else if (IsDataCapture90DebugAndTests(path, asset)) dataCapture90DebugAndTests.Add(asset);
                else otherSOs.Add(asset);
            }

            UnityEditor.EditorUtility.SetDirty(this);
        }

        private string[] GetValidSearchFolders()
        {
            var folders = new List<string>();

            foreach (var folder in searchFolders)
            {
                if (!string.IsNullOrWhiteSpace(folder)
                    && UnityEditor.AssetDatabase.IsValidFolder(folder)
                    && !folders.Contains(folder))
                {
                    folders.Add(folder);
                }
            }

            if (folders.Count == 0)
            {
                folders.Add("Assets");
            }

            return folders.ToArray();
        }

        private static bool IsLegacyAssetPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && path.IndexOf("/Legacy/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsDataCapture00SessionControl(string path, ScriptableObject asset)
        {
            return IsInStage(path, "00_SessionControl")
                || asset is SObasic.CurrentQueueBridge.RecordingSessionStateSO
                || asset is SObasic.CurrentQueueBridge.RecordingToggleRequestSO
                || asset is OutputRouteGateSO
                || asset is PCDiscoveryRequestSO
                || asset is PCReceiverConnectionStatusSO;
        }

        private static bool IsDataCapture10CurrentSOInputs(string path, ScriptableObject asset)
        {
            return IsInStage(path, "10_CurrentSOInputs")
                || asset is CurrentCameraImageSO
                || asset is CurrentCameraFrameTimingSO
                || asset is CurrentCameraPoseSO
                || asset is CurrentCameraMetadataSO
                || asset is CurrentCameraStreamStateSO
                || asset is CurrentVirtualLayerFrameSO
                || asset is CurrentControllerPoseSO
                || asset is CurrentHeadsetPoseSO
                || asset is CurrentNetworkDeviceSO
                || asset is WorldCoordinateFrameSO
                || asset is SessionCoordinateCalibrationSO
                || asset is CoordinateCalibrationResetRequestSO;
        }

        private static bool IsDataCapture20QueueBuffers(string path, ScriptableObject asset)
        {
            return IsInStage(path, "20_QueueBuffers")
                || asset is CameraImageQueueSO
                || asset is CameraFrameTimingQueueSO
                || asset is CameraPoseQueueSO
                || asset is CameraMetadataQueueSO
                || asset is CameraStreamStateQueueSO
                || asset is VirtualLayerQueueSO
                || asset is ControllerPoseQueueSO
                || asset is HeadsetPoseQueueSO
                || asset is NetworkDeviceQueueSO;
        }

        private static bool IsDataCapture30TimeSynchronization(string path, ScriptableObject asset)
        {
            return IsInStage(path, "30_TimeSynchronization")
                || asset is SyncConfiguration
                || asset is TimeStampVariable
                || asset is MergedFrameSnapshotQueueSO
                || asset is CompositeAlignmentConfigurationSO
                || asset is TimestampMergerDebugStateSO;
        }

        private static bool IsDataCapture40SingleEncodeProduction(string path, ScriptableObject asset)
        {
            return IsInStage(path, "40_SingleEncodeProduction")
                || asset is CurrentEncodedFrameSO
                || asset is EncodedFrameQueueSO
                || asset is EncoderConfigurationSO
                || asset is EncodingPipelineConfigurationSO
                || asset is CurrentVideoFrameInputSO
                || asset is VideoFrameInputConfigurationSO;
        }

        private static bool IsDataCapture50ProductAssembly(string path, ScriptableObject asset)
        {
            return IsInStage(path, "50_ProductAssembly")
                || asset is CaptureOutputQueueSO
                || asset is CurrentCaptureOutputSO
                || asset is EncodedOutputBindingConfigurationSO;
        }

        private static bool IsDataCapture60Distribution(string path, ScriptableObject asset)
        {
            return IsInStage(path, "60_Distribution")
                || asset is NetworkSenderConfigurationSO
                || asset is CaptureTransmissionGateSO
                || asset is CurrentNetworkPacketSO
                || asset is NetworkPacketQueueSO
                || asset is CaptureOutputConsumerStateSO;
        }

        private static bool IsDataCapture90DebugAndTests(string path, ScriptableObject asset)
        {
            return IsInStage(path, "90_DebugAndTests")
                || asset is DebugImageStreamSettingsSO
                || asset is QueueDebugStateSO
                || asset is SoDrivenMergeLayerTestRequestSO
                || asset is SoDrivenMergeLayerTestStateSO
                || asset is SoDrivenEncodingSwitchTestRequestSO
                || asset is SoDrivenEncodingSwitchTestStateSO
                || asset is SOFieldWriteRequestSO
                || asset is SORegistryListRequestSO;
        }

        private static bool IsInStage(string path, string stageFolder)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string normalized = path.Replace('\\', '/');
            return normalized.IndexOf("/" + stageFolder + "/", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.EndsWith("/" + stageFolder, StringComparison.OrdinalIgnoreCase);
        }

        private readonly struct DiscoveredScriptableObject
        {
            public readonly string Path;
            public readonly ScriptableObject Asset;

            public DiscoveredScriptableObject(string path, ScriptableObject asset)
            {
                Path = path;
                Asset = asset;
            }
        }
#endif

        private static void AddAll<T>(List<ScriptableObject> results, List<T> assets) where T : ScriptableObject
        {
            if (assets == null)
            {
                return;
            }

            foreach (var asset in assets)
            {
                if (asset != null && !results.Contains(asset))
                {
                    results.Add(asset);
                }
            }
        }
    }
}
