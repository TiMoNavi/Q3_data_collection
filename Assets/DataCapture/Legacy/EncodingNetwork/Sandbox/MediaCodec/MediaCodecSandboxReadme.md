# MediaCodec Sandbox

Last updated: 2026-06-12

This folder is legacy / sandbox-only.

It exists to prove Android hardware encoder plumbing:

```text
MediaCodec encoder
  -> input Surface
  -> synthetic EGL pattern frame
  -> encoded H264/H265 bytes
```

It must not be used as evidence that PCA or Unity `RenderTexture` pixels are reaching MediaCodec.

The old pure-color MP4 output is expected for this sandbox because the input comes from Java `encodePatternFrame(...)`, not from Unity texture data.

Current final-video architecture documents:

```text
Assets/DataCapture/Runtime/40_SingleEncodeProduction/EncoderBackends/AndroidMediaCodec/README.zh-CN.md
Assets/DataCapture/Runtime/40_SingleEncodeProduction/EncoderBackends/AndroidMediaCodec/final-architecture-status-validation-2026-06-11.zh-CN.md
```

Current blocker:

```text
Unity VkImage
  -> Vulkan bridge
  -> MediaCodec input Surface backed image
  -> access units
```
