// Copyright (c) Meta Platforms, Inc. and affiliates.

using UnityEngine;

namespace SObasic
{
    /// <summary>
    /// preset object for starting the level at certain checkpoints
    /// </summary>
    public class IntValueAsset : ScriptableObject
    {
        public int value;
        public static implicit operator int(IntValueAsset asset) => asset.value;

        [SerializeField, TextArea]
        string _info;
    }
}
