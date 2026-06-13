# Runtime Video Encoding Analysis

Last updated: 2026-06-12

## Current Answer

The project is no longer just proving that PCA can be recorded to MP4. The final runtime video path is:

```text
CurrentVideoFrameInputSO.inputTexture
  -> Android MediaCodec input Surface
  -> H264/H265 access units
       -> realtime network stream
       -> MediaMuxer MP4
       -> CaptureOutputQueueSO
       -> local and network file-artifact sinks
```

The central abstraction is the Single Encode Access Unit Bus.

## Current Facts

Validated:

- `PassthroughCameraAccess.GetTexture()` can provide real PCA camera frames in Play Mode / Link and Quest Android Player.
- Real PCA texture can be blitted to a staging `RenderTexture`.
- Real PCA staging `RenderTexture` can be recorded to MP4 through InstantReplay.
- PC smoke proved one encoded H264 product can feed both frame-packet records and MP4 remux output.
- Android Java smoke proved one `MediaCodec` output can feed both C# access-unit bytes and `MediaMuxer`.
- Native bridge smoke proved Unity Vulkan `AccessTexture` can see the Unity `RenderTexture`.
- Native bridge smoke proved `MediaCodec.createInputSurface()` can be attached as an `ANativeWindow`.

Not validated yet:

- Unity/PCA/composite pixels reaching the MediaCodec input Surface.
- Non-empty access units from `encodeUnityTextureFrame(...)`.
- MP4 frames produced from Unity/PCA texture through MediaCodec.
- Network and local artifact consumers reading from `CaptureOutputQueueSO`.

## Important Correction

The old pure-color Android MP4 was produced by:

```text
Q3SurfaceVideoEncoder.encodePatternFrame(...)
  -> EglPatternRenderer.drawFrame(...)
```

That path intentionally draws a generated color pattern. It does not consume Unity texture pixels.

`encodeUnityTextureFrame(...)` is still a placeholder and currently returns an empty byte array.

## Current Code Map

Input:

```text
PassthroughCameraFrameWriter.cs
FrameSource/VideoFrameInputResolver.cs
CurrentVideoFrameInputSO.cs
PassthroughCameraLayerCompositor.cs
```

Encoder smoke and bridge:

```text
OutputMode/SingleEncodeStreamAndMp4/UnityDynamicTextureEncoderSmokeRunner.cs
Assets/Plugins/Android/com/q3datacapture/mediacodec/Q3SurfaceVideoEncoder.java
Native/Q3VulkanBridge/q3dc_vulkan_bridge.cpp
```

Output model:

```text
EncodedOutput/CaptureOutputRecord.cs
EncodedOutput/CaptureOutputQueueSO.cs
EncodedOutput/CurrentCaptureOutputSO.cs
EncodedOutput/EncodedOutputMetadataBinder.cs
OutputSink/CaptureOutputConsumerStateSO.cs
```

Current architecture documents:

```text
OutputMode/SingleEncodeStreamAndMp4/README.zh-CN.md
OutputMode/SingleEncodeStreamAndMp4/final-architecture-status-validation-2026-06-11.zh-CN.md
```

## Next Work

The next work item is the GPU bridge:

```text
Unity VkImage
  -> Vulkan copy / blit / render pass
  -> MediaCodec input Surface backed image
  -> MediaCodec drain
```

Only after that works should the project focus on `NetworkFramePacketSender`, `NetworkFileArtifactSender`, and `LocalArtifactStore`.
