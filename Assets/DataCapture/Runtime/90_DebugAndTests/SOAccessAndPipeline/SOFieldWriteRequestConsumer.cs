using System;
using System.Collections.Generic;
using SObasic;
using UnityEngine;

namespace DataCapture.Testing
{
    [DisallowMultipleComponent]
    public class SOFieldWriteRequestConsumer : MonoBehaviour
    {
        [SerializeField] private SOFieldWriteRequestSO request;
        [SerializeField] private SOStateViewer stateViewerRegistry;
        [SerializeField] private List<ScriptableObject> writableAssets = new List<ScriptableObject>();
        [SerializeField] private bool consumeRequestAfterHandling = true;

        private int lastHandledRevision;
        private bool lastRequestedState;

        private void OnEnable()
        {
            if (request != null)
            {
                lastHandledRevision = request.requestRevision;
            }

            lastRequestedState = false;
        }

        private void Reset()
        {
            stateViewerRegistry = GetComponent<SOStateViewer>();
        }

        private void Awake()
        {
            if (stateViewerRegistry == null)
            {
                stateViewerRegistry = GetComponent<SOStateViewer>();
            }
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
            ApplyRequest();

            if (consumeRequestAfterHandling && request.lastApplySucceeded)
            {
                request.Clear();
                lastRequestedState = false;
            }
        }

        [ContextMenu("Apply Current Request")]
        public void ApplyRequest()
        {
            if (request == null)
            {
                return;
            }

            if (!TryResolveTarget(out ScriptableObject target, out string resolveError))
            {
                request.MarkFailed(resolveError);
                Debug.LogWarning(resolveError, this);
                return;
            }

            if (string.IsNullOrWhiteSpace(request.fieldPath))
            {
                request.MarkFailed("SO field write request is missing fieldPath.");
                return;
            }

            if (!TrySetField(target, request.fieldPath, out string applyError))
            {
                request.MarkFailed(applyError);
                Debug.LogWarning(applyError, this);
                return;
            }

            string message = "Applied " + target.name + "." + request.fieldPath + " from " + request.requestSource + ".";
            request.MarkApplied(message);
            Debug.Log(message, target);
        }

        private bool TryResolveTarget(out ScriptableObject target, out string error)
        {
            target = request.target;
            error = string.Empty;

            if (target != null)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(request.targetName))
            {
                error = "SO field write request is missing both target reference and targetName.";
                return false;
            }

            foreach (ScriptableObject asset in EnumerateWritableAssets())
            {
                if (asset == null)
                {
                    continue;
                }

                if (string.Equals(asset.name, request.targetName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(asset.GetType().Name, request.targetName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(asset.GetType().FullName, request.targetName, StringComparison.OrdinalIgnoreCase))
                {
                    target = asset;
                    return true;
                }
            }

            error = "No writable SO matched targetName '" + request.targetName + "'.";
            return false;
        }

        private IEnumerable<ScriptableObject> EnumerateWritableAssets()
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

            foreach (ScriptableObject asset in writableAssets)
            {
                yield return asset;
            }
        }

        private bool TrySetField(ScriptableObject target, string fieldPath, out string error)
        {
            if (!SOValueAccessUtility.TryGetMemberType(target, fieldPath, out Type targetType, out error))
            {
                return false;
            }

            if (!TryBuildValue(targetType, out object value, out error))
            {
                return false;
            }

            return SOValueAccessUtility.TryWrite(target, fieldPath, value, out error);
        }

        private bool TryBuildValue(Type targetType, out object value, out string error)
        {
            return SOValueAccessUtility.TryConvertFromSerialized(
                targetType,
                request.stringValue,
                request.boolValue,
                request.intValue,
                request.longValue,
                request.floatValue,
                request.vector2Value,
                request.vector3Value,
                out value,
                out error);
        }
    }
}
