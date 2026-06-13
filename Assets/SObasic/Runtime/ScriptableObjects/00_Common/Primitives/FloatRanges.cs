using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SObasic
{
    /// <summary>
    /// Parses and validates multiple float ranges from a string expression.
    /// Example: "1-5, 8, 11-13" means values in [1,5], exactly 8, or [11,13].
    /// Simplified version from FirstHand.
    /// </summary>
    [Serializable]
    public struct FloatRanges
    {
        [SerializeField]
        private string _range;

        private List<FloatRange> _ranges;

        private static Regex _rangeRegex = new Regex(@"(-?\d+(?:\.\d+)?)\s*(?:-\s*(-?\d+(?:\.\d+)?))?");

        public FloatRanges(string range)
        {
            _range = range;
            _ranges = null;
            Parse();
        }

        public FloatRanges(float min, float max)
        {
            _range = $"{min}-{max}";
            _ranges = null;
            Parse();
        }

        private void Parse()
        {
            if (_ranges != null) return;

            _ranges = new List<FloatRange>();
            if (string.IsNullOrWhiteSpace(_range)) return;

            var matches = _rangeRegex.Matches(_range);
            foreach (Match match in matches)
            {
                if (!match.Success) continue;

                float min = float.Parse(match.Groups[1].Value);
                float max = match.Groups[2].Success ? float.Parse(match.Groups[2].Value) : min;

                _ranges.Add(new FloatRange(min, max));
            }
        }

        /// <summary>
        /// Check if value matches any of the defined ranges.
        /// </summary>
        public bool InRange(float value)
        {
            Parse();
            foreach (var range in _ranges)
            {
                if (range.InRange(value)) return true;
            }
            return false;
        }

        public bool Contains(float value) => InRange(value);
    }
}
