using UnityEngine;

namespace SObasic
{
    /// <summary>
    /// ScriptableObject bool variable that implements IActiveState.
    /// Can be used to control capture start/stop via ActiveStateObserver.
    /// </summary>
    [CreateAssetMenu(fileName = "BoolVariable", menuName = "SObasic/Common/Event/Bool Variable")]
    public class BoolVariable : ScriptableObject, IActiveState
    {
        [SerializeField] private bool _value;

        public bool Active => _value;

        public bool Value
        {
            get => _value;
            set => _value = value;
        }
    }

}
