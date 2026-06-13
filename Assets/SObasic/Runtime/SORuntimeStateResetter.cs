using UnityEngine;

namespace SObasic
{
    [DisallowMultipleComponent]
    public sealed class SORuntimeStateResetter : MonoBehaviour
    {
        [SerializeField] private ScriptableObject[] runtimeStateObjects = new ScriptableObject[0];

        public ScriptableObject[] RuntimeStateObjects => runtimeStateObjects;

        [ContextMenu("Reset SO Runtime State")]
        public void ResetRuntimeState()
        {
            SORuntimeStateResetUtility.ResetAll(runtimeStateObjects, this);
        }

        public void ResetRuntimeStateFromMessage(string source)
        {
            Debug.Log("[SORuntimeStateReset] trigger source=\"" + source + "\"", this);
            ResetRuntimeState();
        }
    }
}
