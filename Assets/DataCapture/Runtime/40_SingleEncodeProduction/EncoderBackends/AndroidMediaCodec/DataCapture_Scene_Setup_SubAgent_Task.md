# DataCapture Scene Setup Task

Last updated: 2026-06-12

This file replaces an older scene-setup task note. The old note referenced historical SO names and
legacy MediaCodec sandbox steps. Do not use it as the current setup checklist.

Current final-video source of truth:

```text
Assets/DataCapture/Runtime/40_SingleEncodeProduction/EncoderBackends/AndroidMediaCodec/README.zh-CN.md
Assets/DataCapture/Runtime/40_SingleEncodeProduction/EncoderBackends/AndroidMediaCodec/final-architecture-status-validation-2026-06-11.zh-CN.md
Assets/DataCapture/Runtime/40_SingleEncodeProduction/EncoderBackends/AndroidMediaCodec/runtime-video-encoding-analysis.md
```

Current active validation scene:

```text
Assets/Scenes/SingleEncodePcSmoke.unity
  UnityDynamicTextureEncoderSmoke_Runtime.runOnStart = true
```

Current goal:

```text
Unity dynamic RenderTexture
  -> Vulkan bridge
  -> MediaCodec input Surface
  -> non-empty access units
  -> MediaMuxer MP4
```

After this passes, switch the input to `CurrentVideoFrameInputSO.inputTexture` for raw PCA and then
PCA + Unity composite.
