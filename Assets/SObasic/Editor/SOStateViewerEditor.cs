using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using SObasic;
using SObasic.CurrentQueueBridge;
using DataCapture.Diagnostics;
using DataCapture.Networking;
using DataCapture.Passthrough;
using DataCapture.Synchronization;
using DataCapture.Testing;

namespace DataCapture
{
    [CustomEditor(typeof(SOStateViewer))]
    public class SOStateViewerEditor : Editor
    {
        private const float MinTexturePreviewHeight = 80f;
        private const float MaxTexturePreviewHeight = 240f;

        private static readonly Dictionary<int, bool> foldoutStates = new Dictionary<int, bool>();

        private bool discoverySettingsExpanded;
        private bool pendingAutoDiscover;

        private void OnEnable()
        {
            pendingAutoDiscover = true;
            EditorApplication.projectChanged += QueueAutoDiscover;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= QueueAutoDiscover;
        }

        private void QueueAutoDiscover()
        {
            pendingAutoDiscover = true;
        }

        public override void OnInspectorGUI()
        {
            var viewer = (SOStateViewer)target;

            RunPendingAutoDiscover(viewer);

            serializedObject.UpdateIfRequiredOrScript();

            discoverySettingsExpanded = EditorGUILayout.Foldout(
                discoverySettingsExpanded,
                "Discovery Settings",
                true);

            if (discoverySettingsExpanded)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("autoDiscoverInEditor"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("searchFolders"), true);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("showAdvancedDetails"));

            if (serializedObject.ApplyModifiedProperties())
            {
                pendingAutoDiscover = true;
            }

            if (GUILayout.Button("Refresh SO List Now", GUILayout.Height(30)))
            {
                viewer.AutoDiscoverSOs();
                pendingAutoDiscover = false;
            }

            EditorGUILayout.Space(10);

            DrawStatusOverview(viewer);

            bool showAdvancedDetails = viewer.showAdvancedDetails;

            DrawScriptableObjectPreviewList("DataCapture 00 Session Control", viewer.dataCapture00SessionControl, showAdvancedDetails);
            DrawScriptableObjectPreviewList("DataCapture 10 Current SO Inputs", viewer.dataCapture10CurrentSOInputs, showAdvancedDetails);
            DrawScriptableObjectPreviewList("DataCapture 40 Single Encode Production", viewer.dataCapture40SingleEncodeProduction, showAdvancedDetails, asset => showAdvancedDetails || !IsQueueAsset(asset));
            DrawScriptableObjectPreviewList("DataCapture 50 Product Assembly", viewer.dataCapture50ProductAssembly, showAdvancedDetails, asset => showAdvancedDetails || !IsQueueAsset(asset));
            DrawScriptableObjectPreviewList("DataCapture 60 Distribution", viewer.dataCapture60Distribution, showAdvancedDetails, asset => showAdvancedDetails || !IsQueueAsset(asset));

            if (showAdvancedDetails)
            {
                DrawScriptableObjectPreviewList("DataCapture 20 Queue Buffers", viewer.dataCapture20QueueBuffers, true);
                DrawScriptableObjectPreviewList("DataCapture 30 Time Synchronization", viewer.dataCapture30TimeSynchronization, true);
                DrawScriptableObjectPreviewList("DataCapture 90 Debug And Tests", viewer.dataCapture90DebugAndTests, true);
                DrawScriptableObjectPreviewList("Passthrough Diagnostics", ToScriptableObjectList(viewer.passthroughStates), true);
                DrawScriptableObjectPreviewList("Other ScriptableObjects", viewer.otherSOs, true);
            }
            else
            {
                DrawAdvancedHint(viewer);
            }

            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void RunPendingAutoDiscover(SOStateViewer viewer)
        {
            if (!viewer.autoDiscoverInEditor || Application.isPlaying || !pendingAutoDiscover)
            {
                return;
            }

            viewer.AutoDiscoverSOs();
            pendingAutoDiscover = false;
        }

        private static void DrawStatusOverview(SOStateViewer viewer)
        {
            EditorGUILayout.LabelField("Status / Blocking Issues", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            int lineCount = 0;

            var recordingState = FindFirst<RecordingSessionStateSO>(viewer.dataCapture00SessionControl);
            if (recordingState != null)
            {
                EditorGUILayout.LabelField("Recording State", recordingState.State.ToString());
                lineCount++;

                if (recordingState.HasException)
                {
                    EditorGUILayout.HelpBox(
                        $"Recording Exception: {recordingState.LastExceptionReason}",
                        MessageType.Error);
                    lineCount++;
                }
            }

            var pcReceiverStatus = FindFirst<PCReceiverConnectionStatusSO>(viewer.dataCapture00SessionControl);
            if (pcReceiverStatus != null)
            {
                string pcSummary = pcReceiverStatus.CanStartRecording
                    ? $"PC receiver paired: {pcReceiverStatus.remoteHost}:{pcReceiverStatus.metadataPort}/{pcReceiverStatus.videoPort}"
                    : pcReceiverStatus.GetRecordingBlockReason();

                EditorGUILayout.HelpBox(
                    $"PC Receiver: {pcReceiverStatus.phase} - {pcSummary}",
                    pcReceiverStatus.CanStartRecording ? MessageType.Info : MessageType.Warning);
                lineCount++;

                if (!string.IsNullOrWhiteSpace(pcReceiverStatus.lastErrorMessage))
                {
                    EditorGUILayout.HelpBox(
                        $"PC Receiver Error: {pcReceiverStatus.lastErrorMessage}",
                        MessageType.Error);
                    lineCount++;
                }
            }

            var mergerDebug = FindFirst<TimestampMergerDebugStateSO>(viewer.dataCapture30TimeSynchronization);
            if (mergerDebug != null)
            {
                if (!string.IsNullOrWhiteSpace(mergerDebug.latestDropReason))
                {
                    EditorGUILayout.HelpBox(
                        $"Timestamp Merger: {mergerDebug.latestDropReason}",
                        MessageType.Warning);
                    lineCount++;
                }
                else if (!string.IsNullOrWhiteSpace(mergerDebug.statusMessage))
                {
                    EditorGUILayout.LabelField("Timestamp Merger", mergerDebug.statusMessage);
                    lineCount++;
                }
            }

            var mergeLayerTestState = FindFirst<SoDrivenMergeLayerTestStateSO>(viewer.dataCapture90DebugAndTests);
            if (mergeLayerTestState != null)
            {
                MessageType messageType = mergeLayerTestState.hasFailure
                    ? MessageType.Error
                    : mergeLayerTestState.isRunning ? MessageType.Info : MessageType.None;

                EditorGUILayout.HelpBox(
                    $"SO Merge Test: {mergeLayerTestState.phase} - {mergeLayerTestState.statusMessage}",
                    messageType);
                lineCount++;
            }

            var encodingSwitchTestState = FindFirst<SoDrivenEncodingSwitchTestStateSO>(viewer.dataCapture90DebugAndTests);
            if (encodingSwitchTestState != null)
            {
                MessageType messageType = encodingSwitchTestState.hasFailure
                    ? MessageType.Error
                    : encodingSwitchTestState.isRunning ? MessageType.Info : MessageType.None;

                EditorGUILayout.HelpBox(
                    $"SO Encoding Switch Test: {encodingSwitchTestState.phase} - {encodingSwitchTestState.statusMessage}",
                    messageType);
                lineCount++;
            }

            var writeRequest = FindFirst<SOFieldWriteRequestSO>(viewer.dataCapture90DebugAndTests);
            if (writeRequest != null && !string.IsNullOrWhiteSpace(writeRequest.lastStatusMessage))
            {
                EditorGUILayout.HelpBox(
                    $"SO Write Request: {writeRequest.lastStatusMessage}",
                    writeRequest.lastApplySucceeded ? MessageType.Info : MessageType.Warning);
                lineCount++;
            }

            foreach (var asset in viewer.dataCapture90DebugAndTests)
            {
                if (asset is QueueDebugStateSO queueDebug
                    && (!queueDebug.isHealthy || !string.IsNullOrWhiteSpace(queueDebug.statusMessage)))
                {
                    EditorGUILayout.HelpBox(
                        $"{queueDebug.name}: {queueDebug.statusMessage}",
                        queueDebug.isHealthy ? MessageType.Info : MessageType.Warning);
                    lineCount++;
                }
            }

            foreach (var passthroughState in viewer.passthroughStates)
            {
                if (passthroughState == null)
                {
                    continue;
                }

                bool hasBlocker = !string.IsNullOrWhiteSpace(passthroughState.blockerSummary)
                    && passthroughState.blockerSummary != "None";
                if (!passthroughState.isLikelyVisible && hasBlocker)
                {
                    EditorGUILayout.HelpBox(
                        $"Passthrough: {passthroughState.blockerSummary}",
                        MessageType.Warning);
                    lineCount++;
                }
            }

            foreach (var asset in viewer.otherSOs)
            {
                if (asset is StringVariable stringVariable && !string.IsNullOrWhiteSpace(stringVariable.Value))
                {
                    EditorGUILayout.HelpBox(
                        $"{stringVariable.name}: {stringVariable.Value}",
                        MessageType.Warning);
                    lineCount++;
                }
            }

            if (lineCount == 0)
            {
                EditorGUILayout.HelpBox("No active status data found yet. Enter Play Mode or click Refresh SO List Now.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8f);
        }

        private static void DrawAdvancedHint(SOStateViewer viewer)
        {
            int hiddenCount = CountAssets(viewer.dataCapture20QueueBuffers)
                + CountAssets(viewer.dataCapture30TimeSynchronization)
                + CountAssets(viewer.dataCapture90DebugAndTests)
                + CountAssets(viewer.passthroughStates)
                + CountAssets(viewer.otherSOs)
                + CountQueueAssets(viewer.dataCapture40SingleEncodeProduction)
                + CountQueueAssets(viewer.dataCapture50ProductAssembly)
                + CountQueueAssets(viewer.dataCapture60Distribution);

            if (hiddenCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{hiddenCount} advanced SO entries are hidden. Enable Show Advanced Details to inspect queues, synchronization, diagnostics, passthrough, and miscellaneous SOs.",
                    MessageType.Info);
            }
        }

        private static void DrawScriptableObjectPreviewList(
            string label,
            List<ScriptableObject> assets,
            bool showAdvancedDetails,
            System.Predicate<ScriptableObject> filter = null)
        {
            if (assets == null || assets.Count == 0)
            {
                return;
            }

            int visibleCount = 0;
            int hiddenCount = 0;
            foreach (var asset in assets)
            {
                if (filter == null || filter(asset))
                {
                    visibleCount++;
                }
                else
                {
                    hiddenCount++;
                }
            }

            if (visibleCount == 0)
            {
                return;
            }

            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            if (hiddenCount > 0)
            {
                EditorGUILayout.LabelField(
                    $"{hiddenCount} queue/detail entries hidden until Show Advanced Details is enabled.",
                    EditorStyles.miniLabel);
            }

            foreach (var asset in assets)
            {
                if (filter != null && !filter(asset))
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawScriptableObjectPreview(asset, showAdvancedDetails);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(5);
        }

        private static void DrawScriptableObjectPreview(ScriptableObject asset, bool showAdvancedDetails)
        {
            if (asset == null)
            {
                EditorGUILayout.HelpBox("Missing or deleted preview ScriptableObject.", MessageType.Info);
                return;
            }

            var key = asset.GetInstanceID();
            if (!foldoutStates.TryGetValue(key, out var isExpanded))
            {
                isExpanded = false;
            }

            foldoutStates[key] = EditorGUILayout.Foldout(
                isExpanded,
                $"{asset.name} ({asset.GetType().Name})",
                true);

            if (!foldoutStates[key])
            {
                return;
            }

            EditorGUI.indentLevel++;

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Asset", asset, typeof(ScriptableObject), false);
                EditorGUILayout.LabelField("Type", asset.GetType().Name);
                EditorGUILayout.LabelField("Path", AssetDatabase.GetAssetPath(asset), EditorStyles.wordWrappedMiniLabel);
            }

            var assetObject = new SerializedObject(asset);
            assetObject.UpdateIfRequiredOrScript();

            DrawTexturePreviewIfAvailable(assetObject);

            if (!showAdvancedDetails && !IsEditableCoreAsset(asset))
            {
                DrawCompactFields(assetObject);
                EditorGUI.indentLevel--;
                return;
            }

            var property = assetObject.GetIterator();
            var enterChildren = true;
            var displayedFields = 0;

            EditorGUI.BeginChangeCheck();

            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (property.propertyPath == "m_Script")
                {
                    continue;
                }

                EditorGUILayout.PropertyField(property, true);
                displayedFields++;
            }

            if (displayedFields == 0)
            {
                EditorGUILayout.LabelField("No serialized fields", EditorStyles.miniLabel);
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(asset, "Edit ScriptableObject");
                assetObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
            }
            else
            {
                assetObject.ApplyModifiedProperties();
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawCompactFields(SerializedObject assetObject)
        {
            string[] fieldNames =
            {
                "isValid",
                "frameId",
                "encodedFrameId",
                "sourceCameraFrameId",
                "timestampUnixMs",
                "timestampUtc",
                "currentTimestamp",
                "elapsedSeconds",
                "streamName",
                "sequenceId",
                "byteLength",
                "sourceDeviceId",
                "phase",
                "pcConnected",
                "portsPaired",
                "lastStatusMessage",
                "lastErrorMessage",
                "lastBlocker",
                "isRunning",
                "isComplete",
                "hasFailure",
                "latestStatus",
                "latestIsSendable",
                "latestDropReason",
                "statusMessage",
                "stopReason",
                "observedMergedFrameId",
                "observedSendableCount",
                "observedDebugImageFrames",
                "observedMjpegVideoFrames",
                "observedDebugImageInDualMode",
                "observedMjpegVideoInDualMode",
                "lastObservedCodec",
                "lastApplySucceeded",
                "lastStatusMessage",
                "isLikelyVisible",
                "blockerSummary"
            };

            int displayedFields = 0;
            using (new EditorGUI.DisabledScope(true))
            {
                foreach (string fieldName in fieldNames)
                {
                    var property = assetObject.FindProperty(fieldName);
                    if (property == null)
                    {
                        continue;
                    }

                    EditorGUILayout.PropertyField(property, true);
                    displayedFields++;
                }
            }

            if (displayedFields == 0)
            {
                EditorGUILayout.LabelField("Compact view has no summary fields. Enable Show Advanced Details for full fields.", EditorStyles.miniLabel);
            }
        }

        private static void DrawTexturePreviewIfAvailable(SerializedObject assetObject)
        {
            Texture previewTexture = FindPreviewTexture(assetObject);
            if (previewTexture == null)
            {
                return;
            }

            float textureWidth = previewTexture.width > 0 ? previewTexture.width : 16f;
            float textureHeight = previewTexture.height > 0 ? previewTexture.height : 9f;
            float aspect = textureWidth / textureHeight;
            float availableWidth = Mathf.Max(64f, EditorGUIUtility.currentViewWidth - 80f);
            float previewHeight = Mathf.Clamp(
                availableWidth / Mathf.Max(0.01f, aspect),
                MinTexturePreviewHeight,
                MaxTexturePreviewHeight);

            EditorGUILayout.LabelField("Texture Preview", EditorStyles.miniBoldLabel);
            Rect rect = GUILayoutUtility.GetRect(availableWidth, previewHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawPreviewTexture(rect, previewTexture, null, ScaleMode.ScaleToFit);
            EditorGUILayout.Space(4f);
        }

        private static Texture FindPreviewTexture(SerializedObject assetObject)
        {
            var property = assetObject.GetIterator();
            var enterChildren = true;

            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (property.propertyPath == "m_Script")
                {
                    continue;
                }

                if (property.propertyType == SerializedPropertyType.ObjectReference
                    && property.objectReferenceValue is Texture texture)
                {
                    return texture;
                }
            }

            return null;
        }

        private static bool IsEditableCoreAsset(ScriptableObject asset)
        {
            return asset is SyncConfiguration
                || asset is RecordingSessionStateSO
                || asset is RecordingToggleRequestSO
                || asset is WorldCoordinateFrameSO
                || asset is SessionCoordinateCalibrationSO
                || asset is CoordinateCalibrationResetRequestSO
                || asset is EncoderConfigurationSO
                || asset is EncodingPipelineConfigurationSO
                || asset is DebugImageStreamSettingsSO
                || asset is PCDiscoveryRequestSO
                || asset is NetworkSenderConfigurationSO
                || asset is SoDrivenMergeLayerTestRequestSO
                || asset is SoDrivenEncodingSwitchTestRequestSO
                || asset is SOFieldWriteRequestSO
                || asset is SORegistryListRequestSO;
        }

        private static bool IsQueueAsset(ScriptableObject asset)
        {
            return asset is CameraImageQueueSO
                || asset is CameraFrameTimingQueueSO
                || asset is CameraPoseQueueSO
                || asset is CameraMetadataQueueSO
                || asset is CameraStreamStateQueueSO
                || asset is VirtualLayerQueueSO
                || asset is ControllerPoseQueueSO
                || asset is HeadsetPoseQueueSO
                || asset is NetworkDeviceQueueSO
                || asset is MergedFrameSnapshotQueueSO
                || asset is EncodedFrameQueueSO
                || asset is CaptureOutputQueueSO
                || asset is NetworkPacketQueueSO;
        }

        private static T FindFirst<T>(List<ScriptableObject> assets) where T : ScriptableObject
        {
            if (assets == null)
            {
                return null;
            }

            foreach (var asset in assets)
            {
                if (asset is T typedAsset)
                {
                    return typedAsset;
                }
            }

            return null;
        }

        private static int CountAssets<T>(List<T> assets)
        {
            return assets == null ? 0 : assets.Count;
        }

        private static int CountQueueAssets(List<ScriptableObject> assets)
        {
            if (assets == null)
            {
                return 0;
            }

            int count = 0;
            foreach (var asset in assets)
            {
                if (IsQueueAsset(asset))
                {
                    count++;
                }
            }

            return count;
        }

        private static List<ScriptableObject> ToScriptableObjectList<T>(List<T> assets) where T : ScriptableObject
        {
            var result = new List<ScriptableObject>();
            if (assets == null)
            {
                return result;
            }

            foreach (var asset in assets)
            {
                result.Add(asset);
            }

            return result;
        }
    }
}
