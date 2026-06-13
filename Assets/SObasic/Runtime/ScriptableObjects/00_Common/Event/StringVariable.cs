using UnityEngine;

namespace SObasic
{
    [CreateAssetMenu(fileName = "StringVariable", menuName = "SObasic/Common/Event/String Variable")]
    public class StringVariable : ScriptableObject
    {
        [SerializeField] private string _value;

        public string Value
        {
            get => _value;
            set => _value = value;
        }

        public void Clear()
        {
            _value = string.Empty;
        }
    }
}
