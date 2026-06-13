# Debug Image Path And Final Video Path

Last updated: 2026-06-12

This document replaces the old phase-4 planning notes.

Current source of truth for the final video architecture:

```text
Assets/DataCapture/Networking/Encoding/OutputMode/SingleEncodeStreamAndMp4/README.zh-CN.md
Assets/DataCapture/Networking/Encoding/OutputMode/SingleEncodeStreamAndMp4/final-architecture-status-validation-2026-06-11.zh-CN.md
Assets/DataCapture/Networking/Encoding/runtime-video-encoding-analysis.md
```

## Current Split

There are two different paths and they must not be confused.

Debug image path:

```text
CurrentVideoFrameInputSO.inputTexture
  -> AsyncGPUReadback
  -> JPEG
  -> low-frequency debug packet
```

This path is useful for visual diagnostics. It is not the final video stream.

Final video path:

```text
CurrentVideoFrameInputSO.inputTexture
  -> MediaCodec input Surface
  -> H264/H265 access units
  -> Single Encode Access Unit Bus
```

## Current Correction

The old recommendation to keep validating with pure-color or EGL test-pattern frames is no longer the main path.

Synthetic pattern frames already proved the MediaCodec + MediaMuxer plumbing. The remaining work is the real Unity texture bridge:

```text
Unity VkImage
  -> Vulkan bridge
  -> MediaCodec input Surface backed image
  -> access units
```

## What Not To Infer

Do not infer PCA success or failure from Java pattern MP4 files.

Do not use InstantReplay as the final video architecture. It remains useful only as proof that real PCA texture can be recorded into an MP4 file.
