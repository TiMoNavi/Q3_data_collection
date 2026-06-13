using System;
using UnityEngine;

namespace SObasic
{
    /// <summary>
    /// Represents a range of float values with min/max bounds.
    /// Simplified version from FirstHand for general use.
    /// </summary>
    [Serializable]
    public class FloatRange
    {
        [SerializeField]
        private float _min = float.NegativeInfinity;

        [SerializeField]
        private float _max = float.PositiveInfinity;

        public float Min => _min;
        public float Max => _max;

        public FloatRange() { }

        public FloatRange(float min, float max)
        {
            _min = min;
            _max = max;
        }

        /// <summary>
        /// Check if a value is within this range (inclusive).
        /// </summary>
        public bool InRange(float value)
        {
            return value >= _min && value <= _max;
        }
    }
}
