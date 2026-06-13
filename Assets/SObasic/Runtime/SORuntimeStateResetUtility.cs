using System;
using System.Reflection;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SObasic
{
    public static class SORuntimeStateResetUtility
    {
        private static readonly string[] ResetMethodPriority =
        {
            "Clear",
            "ClearQueue",
            "ResetRuntimeState"
        };

        public static int ResetAll(ScriptableObject[] scriptableObjects, UnityEngine.Object logContext = null)
        {
            if (scriptableObjects == null)
            {
                LogFailure(null, "<none>", "scriptableObjects array is null", logContext);
                return 0;
            }

            int resetCount = 0;
            foreach (ScriptableObject scriptableObject in scriptableObjects)
            {
                if (TryReset(scriptableObject, logContext))
                {
                    resetCount++;
                }
            }

            return resetCount;
        }

        public static bool TryReset(ScriptableObject scriptableObject, UnityEngine.Object logContext = null)
        {
            if (scriptableObject == null)
            {
                LogFailure(null, "<none>", "scriptableObject is null", logContext);
                return false;
            }

            MethodInfo resetMethod = FindResetMethod(scriptableObject.GetType());
            if (resetMethod == null)
            {
                LogFailure(scriptableObject, "<none>", "No supported runtime reset method found. Expected Clear(), ClearQueue(), or ResetRuntimeState().", logContext);
                return false;
            }

            try
            {
                resetMethod.Invoke(scriptableObject, null);
                LogSuccess(scriptableObject, resetMethod.Name, logContext);
                return true;
            }
            catch (TargetInvocationException exception)
            {
                Exception rootException = exception.InnerException ?? exception;
                LogFailure(scriptableObject, resetMethod.Name, rootException.GetType().Name + ": " + rootException.Message, logContext);
                return false;
            }
            catch (Exception exception)
            {
                LogFailure(scriptableObject, resetMethod.Name, exception.GetType().Name + ": " + exception.Message, logContext);
                return false;
            }
        }

        private static MethodInfo FindResetMethod(Type type)
        {
            foreach (string methodName in ResetMethodPriority)
            {
                MethodInfo method = type.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    Type.EmptyTypes,
                    null);

                if (method != null && !method.IsGenericMethod)
                {
                    return method;
                }
            }

            return null;
        }

        private static void LogSuccess(ScriptableObject scriptableObject, string methodName, UnityEngine.Object logContext)
        {
            Debug.Log(
                "[SORuntimeStateReset] success " +
                "assetName=\"" + GetAssetName(scriptableObject) + "\" " +
                "assetPath=\"" + GetAssetPath(scriptableObject) + "\" " +
                "method=\"" + methodName + "\"",
                logContext != null ? logContext : scriptableObject);
        }

        private static void LogFailure(ScriptableObject scriptableObject, string methodName, string error, UnityEngine.Object logContext)
        {
            Debug.Log(
                "[SORuntimeStateReset] failed " +
                "assetName=\"" + GetAssetName(scriptableObject) + "\" " +
                "assetPath=\"" + GetAssetPath(scriptableObject) + "\" " +
                "method=\"" + methodName + "\" " +
                "error=\"" + error + "\"",
                logContext != null ? logContext : scriptableObject);
        }

        private static string GetAssetName(ScriptableObject scriptableObject)
        {
            return scriptableObject != null ? scriptableObject.name : "<null>";
        }

        private static string GetAssetPath(ScriptableObject scriptableObject)
        {
            if (scriptableObject == null)
            {
                return "<null>";
            }

#if UNITY_EDITOR
            string assetPath = AssetDatabase.GetAssetPath(scriptableObject);
            return string.IsNullOrEmpty(assetPath) ? "<not-asset>" : assetPath;
#else
            return "<runtime-unavailable>";
#endif
        }
    }
}
