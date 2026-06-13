// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using UnityEngine;

namespace SObasic
{
    /// <summary>
    /// Updates FloatProperty value based on a float value
    /// </summary>
    public class FloatProperty : MonoBehaviour, IProperty<float>
    {
        public float Value
        {
            get => _value;
            set
            {
                _value = value;
                WhenChanged?.Invoke();
            }
        }

        [SerializeField]
        private float _value;

        public event Action WhenChanged;
    }
}
