using DataCapture.Networking;
using UnityEditor;
using UnityEngine;

namespace DataCapture
{
    [CustomEditor(typeof(PCReceiverConnectionStatusSO))]
    public class PCReceiverConnectionStatusSOEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var status = (PCReceiverConnectionStatusSO)target;
            EditorGUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Request Discovery", GUILayout.Height(28)))
                {
                    Undo.RecordObject(status, "Request PC Receiver Discovery");
                    status.RequestDiscovery("PCReceiverConnectionStatusSO Inspector");
                    EditorUtility.SetDirty(status);
                    PCReceiverDiscoveryEditorBridge.StartSceneDiscoveryClients();
                }

                if (GUILayout.Button("Stop Discovery", GUILayout.Height(28)))
                {
                    Undo.RecordObject(status, "Stop PC Receiver Discovery");
                    status.StopDiscoveryRequest("PCReceiverConnectionStatusSO Inspector");
                    EditorUtility.SetDirty(status);
                    PCReceiverDiscoveryEditorBridge.StopSceneDiscoveryClients();
                }
            }
        }
    }
}
