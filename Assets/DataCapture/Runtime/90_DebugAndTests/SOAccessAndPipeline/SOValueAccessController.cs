using System;
using System.Collections.Generic;
using System.Text;
using SObasic;
using UnityEngine;

namespace DataCapture.Testing
{
    [DisallowMultipleComponent]
    public class SOValueAccessController : MonoBehaviour
    {
        [Header("Registry")]
        [SerializeField] private SOStateViewer stateViewerRegistry;
        [SerializeField] private List<ScriptableObject> additionalAssets = new List<ScriptableObject>();

        [Header("Inspector Operation")]
        [SerializeField] private string targetName = string.Empty;
        [SerializeField] private string fieldPath = string.Empty;
        [SerializeField] private SOFieldWriteValueType valueType;
        [SerializeField] private string stringValue = string.Empty;
        [SerializeField] private bool boolValue;
        [SerializeField] private int intValue;
        [SerializeField] private long longValue;
        [SerializeField] private float floatValue;
        [SerializeField] private Vector2 vector2Value;
        [SerializeField] private Vector3 vector3Value;

        [Header("Result")]
        [SerializeField] private bool logOperationsToUnity = true;
        [SerializeField] private bool lastSucceeded;
        [SerializeField] private string lastTargetName = string.Empty;
        [SerializeField] private string lastFieldPath = string.Empty;
        [SerializeField] private string lastValueText = string.Empty;
        [SerializeField] private string lastStatusMessage = string.Empty;
        [SerializeField] private long lastChangedUnixMs;

        public bool LastSucceeded => lastSucceeded;
        public string LastValueText => lastValueText;
        public string LastStatusMessage => lastStatusMessage;

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

        [ContextMenu("Read Inspector Field")]
        public void ReadInspectorField()
        {
            TryReadValue(targetName, fieldPath, out _, out _, out _);
        }

        [ContextMenu("Write Inspector Field")]
        public void WriteInspectorField()
        {
            TryWriteSerializedValue(
                targetName,
                fieldPath,
                stringValue,
                boolValue,
                intValue,
                longValue,
                floatValue,
                vector2Value,
                vector3Value,
                out _);
        }

        [ContextMenu("Log Registered SO List")]
        public void LogRegisteredSOList()
        {
            string listText = BuildRegisteredSOList(true, true, out int count);
            Debug.Log("[SO-Access][LIST] SOValueAccessController registered " + count + " asset(s).\n" + listText, this);
        }

        public int CopyRegisteredAssets(List<ScriptableObject> results)
        {
            if (results == null)
            {
                return 0;
            }

            int added = 0;
            foreach (ScriptableObject asset in EnumerateRegisteredAssets())
            {
                if (asset != null && !results.Contains(asset))
                {
                    results.Add(asset);
                    added++;
                }
            }

            return added;
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

        public bool TryReadValue(
            string requestedTargetName,
            string requestedFieldPath,
            out object value,
            out string valueText,
            out string error)
        {
            value = null;
            valueText = string.Empty;

            if (!TryResolveTarget(requestedTargetName, out ScriptableObject target, out error))
            {
                MarkResult(false, requestedTargetName, requestedFieldPath, string.Empty, error);
                return false;
            }

            if (!SOValueAccessUtility.TryRead(target, requestedFieldPath, out value, out error))
            {
                MarkResult(false, target.name, requestedFieldPath, string.Empty, error);
                return false;
            }

            valueText = SOValueAccessUtility.FormatValue(value);
            MarkResult(true, target.name, requestedFieldPath, valueText, "Read " + target.name + "." + requestedFieldPath + "=" + valueText + ".");
            return true;
        }

        public bool TryWriteValue(string requestedTargetName, string requestedFieldPath, object rawValue, out string error)
        {
            if (!TryResolveTarget(requestedTargetName, out ScriptableObject target, out error))
            {
                MarkResult(false, requestedTargetName, requestedFieldPath, string.Empty, error);
                return false;
            }

            if (!SOValueAccessUtility.TryWrite(target, requestedFieldPath, rawValue, out error))
            {
                MarkResult(false, target.name, requestedFieldPath, string.Empty, error);
                return false;
            }

            MarkAssetDirty(target);

            string valueText = SOValueAccessUtility.FormatValue(rawValue);
            MarkResult(true, target.name, requestedFieldPath, valueText, "Wrote " + target.name + "." + requestedFieldPath + "=" + valueText + ".");
            return true;
        }

        public bool TryWriteSerializedValue(
            string requestedTargetName,
            string requestedFieldPath,
            string serializedStringValue,
            bool serializedBoolValue,
            int serializedIntValue,
            long serializedLongValue,
            float serializedFloatValue,
            Vector2 serializedVector2Value,
            Vector3 serializedVector3Value,
            out string error)
        {
            if (!TryResolveTarget(requestedTargetName, out ScriptableObject target, out error))
            {
                MarkResult(false, requestedTargetName, requestedFieldPath, string.Empty, error);
                return false;
            }

            if (!SOValueAccessUtility.TryGetMemberType(target, requestedFieldPath, out Type targetType, out error))
            {
                MarkResult(false, target.name, requestedFieldPath, string.Empty, error);
                return false;
            }

            if (!SOValueAccessUtility.TryConvertFromSerialized(
                    targetType,
                    serializedStringValue,
                    serializedBoolValue,
                    serializedIntValue,
                    serializedLongValue,
                    serializedFloatValue,
                    serializedVector2Value,
                    serializedVector3Value,
                    out object value,
                    out error))
            {
                MarkResult(false, target.name, requestedFieldPath, string.Empty, error);
                return false;
            }

            return TryWriteValue(target.name, requestedFieldPath, value, out error);
        }

        public bool WriteString(string requestedTargetName, string requestedFieldPath, string value, out string error)
        {
            return TryWriteValue(requestedTargetName, requestedFieldPath, value, out error);
        }

        public bool WriteBool(string requestedTargetName, string requestedFieldPath, bool value, out string error)
        {
            return TryWriteValue(requestedTargetName, requestedFieldPath, value, out error);
        }

        public bool WriteInt(string requestedTargetName, string requestedFieldPath, int value, out string error)
        {
            return TryWriteValue(requestedTargetName, requestedFieldPath, value, out error);
        }

        public bool WriteLong(string requestedTargetName, string requestedFieldPath, long value, out string error)
        {
            return TryWriteValue(requestedTargetName, requestedFieldPath, value, out error);
        }

        public bool WriteFloat(string requestedTargetName, string requestedFieldPath, float value, out string error)
        {
            return TryWriteValue(requestedTargetName, requestedFieldPath, value, out error);
        }

        public void RegisterAsset(ScriptableObject asset)
        {
            if (asset != null && !additionalAssets.Contains(asset))
            {
                additionalAssets.Add(asset);
            }
        }

        public bool TryResolveTarget(string requestedTargetName, out ScriptableObject target, out string error)
        {
            target = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(requestedTargetName))
            {
                error = "SO target name is empty.";
                return false;
            }

            foreach (ScriptableObject asset in EnumerateRegisteredAssets())
            {
                if (asset == null)
                {
                    continue;
                }

                Type type = asset.GetType();
                if (string.Equals(asset.name, requestedTargetName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type.Name, requestedTargetName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type.FullName, requestedTargetName, StringComparison.OrdinalIgnoreCase))
                {
                    target = asset;
                    return true;
                }
            }

            error = "No registered SO matched targetName '" + requestedTargetName + "'.";
            return false;
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

        private void MarkResult(bool succeeded, string resultTargetName, string resultFieldPath, string valueText, string message)
        {
            lastSucceeded = succeeded;
            lastTargetName = resultTargetName ?? string.Empty;
            lastFieldPath = resultFieldPath ?? string.Empty;
            lastValueText = valueText ?? string.Empty;
            lastStatusMessage = message ?? string.Empty;
            lastChangedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (!logOperationsToUnity)
            {
                return;
            }

            string log = "[SO-Access][" + (succeeded ? "OK" : "FAIL") + "] " + lastStatusMessage;
            if (succeeded)
            {
                Debug.Log(log, this);
            }
            else
            {
                Debug.LogWarning(log, this);
            }
        }

        private static void MarkAssetDirty(ScriptableObject target)
        {
#if UNITY_EDITOR
            if (target != null && !Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(target);
            }
#endif
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
