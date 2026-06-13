using System.IO;
using DataCapture.Networking;
using UnityEditor;
using UnityEngine;

namespace DataCapture
{
    [InitializeOnLoad]
    public static class PCReceiverDiscoveryEditorBridge
    {
        private const string StatusAssetPath = "Assets/SOData/DataCapture/00_SessionControl/PCReceiverConnectionStatus.asset";
        private const string TriggerFileName = "DataCapture_RequestPCReceiverDiscovery.txt";
        private static double nextTriggerCheckTime;

        static PCReceiverDiscoveryEditorBridge()
        {
            EditorApplication.update -= CheckTriggerFile;
            EditorApplication.update += CheckTriggerFile;
        }

        [MenuItem("DataCapture/Networking/Request PC Receiver Discovery")]
        public static void RequestPCReceiverDiscovery()
        {
            PCReceiverConnectionStatusSO status =
                AssetDatabase.LoadAssetAtPath<PCReceiverConnectionStatusSO>(StatusAssetPath);
            if (status == null)
            {
                Debug.LogWarning("PC receiver connection status asset was not found at " + StatusAssetPath + ".");
                return;
            }

            Undo.RecordObject(status, "Request PC Receiver Discovery");
            status.RequestDiscovery("Unity Editor Menu");
            EditorUtility.SetDirty(status);
            AssetDatabase.SaveAssets();
            StartSceneDiscoveryClients();
        }

        [MenuItem("DataCapture/Networking/Stop PC Receiver Discovery")]
        public static void StopPCReceiverDiscovery()
        {
            PCReceiverConnectionStatusSO status =
                AssetDatabase.LoadAssetAtPath<PCReceiverConnectionStatusSO>(StatusAssetPath);
            if (status != null)
            {
                Undo.RecordObject(status, "Stop PC Receiver Discovery");
                status.StopDiscoveryRequest("Unity Editor Menu");
                EditorUtility.SetDirty(status);
                AssetDatabase.SaveAssets();
            }

            StopSceneDiscoveryClients();
        }

        public static void StartSceneDiscoveryClients()
        {
            int count = 0;
            foreach (LanDiscoveryClient client in Resources.FindObjectsOfTypeAll<LanDiscoveryClient>())
            {
                if (client == null || EditorUtility.IsPersistent(client))
                {
                    continue;
                }

                client.StartDiscovery();
                count++;
            }

            Debug.Log("Requested PC receiver discovery from " + count + " LanDiscoveryClient instance(s).");
        }

        public static void StopSceneDiscoveryClients()
        {
            foreach (LanDiscoveryClient client in Resources.FindObjectsOfTypeAll<LanDiscoveryClient>())
            {
                if (client == null || EditorUtility.IsPersistent(client))
                {
                    continue;
                }

                client.StopDiscovery();
            }
        }

        private static void CheckTriggerFile()
        {
            if (EditorApplication.timeSinceStartup < nextTriggerCheckTime)
            {
                return;
            }

            nextTriggerCheckTime = EditorApplication.timeSinceStartup + 1.0;
            string triggerFilePath = GetTriggerFilePath();
            if (!File.Exists(triggerFilePath))
            {
                return;
            }

            File.Delete(triggerFilePath);
            RequestPCReceiverDiscovery();
        }

        private static string GetTriggerFilePath()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
            return Path.Combine(projectRoot, "Temp", TriggerFileName);
        }
    }
}
