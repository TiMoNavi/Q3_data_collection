# Camera, Network, Encoding SO Architecture

Last updated: 2026-06-12

This file replaces an older SO architecture draft that used names such as `CurrentCameraFrameSO`.
Those names are historical and should not drive new implementation.

Current final-video source of truth:

```text
Assets/DataCapture/Runtime/40_SingleEncodeProduction/EncoderBackends/AndroidMediaCodec/README.zh-CN.md
Assets/DataCapture/Runtime/40_SingleEncodeProduction/EncoderBackends/AndroidMediaCodec/final-architecture-status-validation-2026-06-11.zh-CN.md
Assets/DataCapture/Runtime/40_SingleEncodeProduction/EncoderBackends/AndroidMediaCodec/runtime-video-encoding-analysis.md
```

## Current Boundary

PCA writes split current SOs:

```text
PassthroughCameraFrameWriter
  -> CurrentCameraImageSO
  -> CurrentCameraStreamStateSO
  -> CurrentCameraPoseSO
  -> CurrentCameraMetadataSO
```

The video encoder should consume the shared video input boundary:

```text
VideoFrameInputResolver
  -> CurrentVideoFrameInputSO
```

The encoder should not directly search for PCA or compositor objects.

## Final Video Target

```text
CurrentVideoFrameInputSO.inputTexture
  -> one MediaCodec encode
  -> H264/H265 access units
       -> network stream
       -> MediaMuxer MP4
       -> CaptureOutputQueueSO
       -> local artifact
       -> file transfer
```

The central abstraction is the Single Encode Access Unit Bus.

## Current Blocker

Validated:

```text
PCA texture exists.
PCA texture can be recorded to MP4 through InstantReplay.
MediaCodec + MediaMuxer can encode synthetic pattern frames.
Unity Vulkan AccessTexture can see a Unity RenderTexture.
MediaCodec input Surface can be attached as an ANativeWindow.
```

Not yet implemented:

```text
Unity VkImage
  -> MediaCodec input Surface pixels
  -> real access units
```
