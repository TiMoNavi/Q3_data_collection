# DataCapture Full Runtime Chain

Last updated: 2026-06-12

This file replaces an older full-chain draft. The old draft used historical names such as
`CurrentCameraFrameSO` and should not be used as the current implementation guide.

Current final-video source of truth:

```text
Assets/DataCapture/Runtime/40_SingleEncodeProduction/EncoderBackends/AndroidMediaCodec/README.zh-CN.md
Assets/DataCapture/Runtime/40_SingleEncodeProduction/EncoderBackends/AndroidMediaCodec/final-architecture-status-validation-2026-06-11.zh-CN.md
Assets/DataCapture/Runtime/40_SingleEncodeProduction/EncoderBackends/AndroidMediaCodec/runtime-video-encoding-analysis.md
```

Current runtime shape:

```text
PassthroughCameraFrameWriter
  -> CurrentCameraImageSO / stream state / pose / metadata
  -> VideoFrameInputResolver
  -> CurrentVideoFrameInputSO
  -> one MediaCodec encode
  -> Single Encode Access Unit Bus
  -> CaptureOutputQueueSO and sinks
```

Current blocker:

```text
Unity VkImage
  -> MediaCodec input Surface pixels
  -> real H264/H265 access units
```
