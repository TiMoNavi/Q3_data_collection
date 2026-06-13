using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DataCapture.Testing
{
    [DisallowMultipleComponent]
    public class SORegistryListResponder : MonoBehaviour
    {
        [SerializeField] private SORegistryListRequestSO request;
        [SerializeField] private SOStateViewer stateViewerRegistry;
        [SerializeField] private List<ScriptableObject> additionalAssets = new List<ScriptableObject>();
        [SerializeField] private bool logListToUnity = true;
        [SerializeField] private bool consumeRequestAfterHandling = true;

        private int lastHandledRevision;
        private bool lastRequestedState;

        private void Reset()
        {
            stateViewerRegistry = GetComponent<SOStateViewer>();
            if (stateViewerRegistry == null)
            {
                stateViewerRegistry = FindFirstObjectByType<SOStateViewer>();
            }
        }

        private void Awake()
        {
            if (stateViewerRegistry == null)
            {
                stateViewerRegistry = GetComponent<SOStateViewer>();
            }
        }

        private void OnEnable()
        {
            if (request != null)
            {
                lastHandledRevision = request.requestRevision;
            }

            lastRequestedState = false;
        }

        private void Update()
        {
            if (request == null)
            {
                return;
            }

            bool revisionChanged = request.requestRevision != lastHandledRevision;
            bool manualBoolRaised = request.requested && !lastRequestedState;
            lastRequestedState = request.requested;

            if (!revisionChanged && !manualBoolRaised)
            {
                return;
            }

            lastHandledRevision = request.requestRevision;
            DumpRegistryToRequest();

            if (consumeRequestAfterHandling)
            {
                request.Clear();
                lastRequestedState = false;
            }
        }

        [ContextMenu("Dump SO Registry")]
        public void DumpRegistryToRequest()
        {
            string listText = BuildRegisteredSOList(
                request == null || request.includeTypeNames,
                request == null || request.includeAssetPaths,
                out int count);

            string message = "SO registry contains " + count + " registered asset(s).";
            request?.MarkResult(true, count, listText, message);

            if (!logListToUnity)
            {
                return;
            }

            Debug.Log("[SO-Access][LIST] " + message + "\n" + listText, this);
        }

        public string BuildRegisteredSOList(bool includeTypeNames, bool includeAssetPaths, out int count)
        {
            var seen = new HashSet<ScriptableObject>();
            var builder = new StringBuilder();
            count = 0;

            foreach (ScriptableObject asset in EnumerateRegisteredAssets())
            {
                if (asset == null || seen.Contains(asset))
                {
                    continue;
                }

                seen.Add(asset);
                count++;
                builder.Append(count);
                builder.Append(". ");
                builder.Append(asset.name);

                if (includeTypeNames)
                {
                    builder.Append(" type=");
                    builder.Append(asset.GetType().FullName);
                }

                if (includeAssetPaths)
                {
                    string path = GetAssetPath(asset);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        builder.Append(" path=");
                        builder.Append(path);
                    }
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }

        public void RegisterAsset(ScriptableObject asset)
        {
            if (asset != null && !additionalAssets.Contains(asset))
            {
                additionalAssets.Add(asset);
            }
        }

        private IEnumerable<ScriptableObject> EnumerateRegisteredAssets()
        {
            if (stateViewerRegistry != null)
            {
                var registryAssets = new List<ScriptableObject>();
                stateViewerRegistry.CopyAllScriptableObjects(registryAssets);
                foreach (ScriptableObject asset in registryAssets)
                {
                    yield return asset;
                }
            }

            foreach (ScriptableObject asset in additionalAssets)
            {
                yield return asset;
            }
        }

        private static string GetAssetPath(ScriptableObject asset)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.GetAssetPath(asset);
#else
            return string.Empty;
#endif
        }
    }
}
