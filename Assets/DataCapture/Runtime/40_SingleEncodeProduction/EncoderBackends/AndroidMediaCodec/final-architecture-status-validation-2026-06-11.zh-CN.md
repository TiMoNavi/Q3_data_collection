# 2026-06-11 Final Architecture Status

Last updated: 2026-06-12 cleanup pass

## 一句话

初始目标需要重新摆正：

```text
不是 PCA -> MP4。

而是：
PCA / Unity composite RenderTexture
  -> one MediaCodec encode
  -> one H264/H265 access unit bus
       -> realtime network stream
       -> MediaMuxer MP4
       -> CaptureOutputQueueSO
       -> local artifact
       -> later file transfer
```

中心抽象是 `Single Encode Access Unit Bus`。MP4 是 sink，不是中心产物。

## 架构原则补充

当前入口文档 `README.zh-CN.md` 已把以下原则列为硬约束：

```text
1. 输入来源可替换：编码器只消费 CurrentVideoFrameInputSO。
2. 编码产物携带上一阶段合成 / 同步元数据。
3. 编码 backend 可替换：MediaCodec H264/H265 是当前主线，但不是唯一形态。
4. 输出层统一：网络和文件 sink 只读 CaptureOutputQueueSO / CaptureOutputRecord。
5. 同一批 access units 同时服务实时视频流、MP4、本地 artifact 和网络/云端接收端。
```

## 当前实机事实

### 1. 真实 PCA -> MP4 已通过

已在 Quest Android Player 上重新跑通：

```text
PCA -> staging RenderTexture -> InstantReplay MP4
```

本地证据：

```text
captures/device_debug_20260611_1506_pca_direct/pca_direct_20260611_070602.mp4
captures/device_debug_20260611_1506_pca_direct/frames/frame10.png
```

`ffprobe` 结果：

```text
codec = h264
resolution = 1280x960
duration = about 5.09s
frame count = 154
```

抽帧确认是真实 PCA 画面，不是纯蓝、纯色或 Java pattern。

这说明 PCA texture 本身不是当前 blocker。InstantReplay 只是原型验证路径，不是最终架构。

### 2. Java MediaCodec + MediaMuxer A0 已通过

历史 smoke：

```text
captures/SingleEncodeAndroidMuxerSmoke_20260610_193429_manifest.json
captures/SingleEncodeAndroidMuxerSmoke_20260610_193429_capture.mp4
```

关键结果：

```text
encoderName = c2.qti.avc.encoder
muxedSampleCount = 60
publishedFramePacketCount = 59
mp4Bytes = 15115
lastInputSourceKind = RawCameraImage
```

这条 smoke 证明同一批 MediaCodec output samples 可以同时：

```text
return byte[] access unit to C#
write sample to MediaMuxer
publish FramePacket / FileArtifact model
```

但它没有编码 PCA texture。它调用的是 `encodePatternFrame(...)`。

### 3. 纯蓝 / 纯色 MP4 已定位

`SingleEncodeAndroidMuxerSmoke_20260610_193429_capture.mp4` 的纯色画面来自 Java synthetic pattern：

```text
Q3SurfaceVideoEncoder.encodePatternFrame(...)
  -> EglPatternRenderer.drawFrame(...)
```

该 renderer 的 fragment shader 只输出测试色：

```text
vec4(t, 0.25, 1.0 - t, 1.0)
```

所以纯色 MP4 的正确解释是：

```text
MediaCodec input Surface path worked.
MediaMuxer path worked.
The real Unity/PCA texture bridge was not used.
```

它不是 PCA camera access 失败，也不是 MP4 mux 链路失败。

### 4. Unity Vulkan bridge 两端已可见

最新实机 dynamic bridge smoke：

```text
captures/device_debug_20260611_1722_unity_dynamic_bridge/manifest.json
```

关键结果：

```text
graphicsDeviceType = Vulkan
nativeBridgeVulkanReady = true
nativeBridgeProbeEventCount = 60
lastAccessTextureOk = true
vkFormat = 43
vkExtent = 320x320x1
encoderSurfaceAttached = true
encoderSurfaceAttachCount = 1
encoderWindowWidth/Height = 320x320
bridgeAttemptCount = 60
bridgeOutputFrameCount = 0
patternFallbackFrameCount = 0
muxedSampleCount = 0
```

结论：

```text
Unity RenderTexture -> IUnityGraphicsVulkan.AccessTexture: passed
MediaCodec input Surface -> ANativeWindow_fromSurface: passed
Unity texture -> encoder Surface pixels: not implemented
```

当前 blocker 已经缩小到 Vulkan GPU 写入步骤。

## 当前代码事实

### 入口场景

当前 active scene：

```text
Assets/Scenes/SingleEncodePcSmoke.unity
```

默认自动运行：

```text
UnityDynamicTextureEncoderSmoke_Runtime.runOnStart = true
fallbackToPatternWhenBridgeUnavailable = false
```

默认不自动运行：

```text
SingleEncodePcSmoke_Runtime.runOnStart = false
SingleEncodeAndroidMuxerSmoke_Runtime.runOnStart = false
PCA_DirectMp4Prototype_Runtime.runOnStart = false
```

这意味着新 APK 默认测试的是 Unity RT bridge smoke，而不是 Java pattern smoke 或 InstantReplay PCA direct prototype。

### 输入层

PCA writer：

```text
PassthroughCameraFrameWriter
  -> cameraAccess.GetTexture()
  -> CurrentCameraImageSO
  -> CurrentCameraStreamStateSO
  -> CurrentCameraPoseSO
  -> CurrentCameraMetadataSO
```

正式视频输入边界：

```text
VideoFrameInputResolver
  -> CurrentVideoFrameInputSO
```

`SingleEncodePcSmoke.unity` 中：

```text
VideoFrameInputConfiguration.sourceKind = PassthroughUnityComposite
fallbackToRawCameraImage = true
VideoFrameInputResolver.compositor = null
```

所以该场景当前会 fallback 到 raw PCA staging texture。`SampleScene.unity` 才接了 compositor。

### Java 编码层

已实现：

```text
Q3SurfaceVideoEncoder.startWithMp4(...)
Q3SurfaceVideoEncoder.encodePatternFrame(...)
Q3SurfaceVideoEncoder.stop()
```

占位：

```text
Q3SurfaceVideoEncoder.encodeUnityTextureFrame(...)
```

该方法现在只验证 native texture pointer 非零并输出状态，然后返回空 byte array。

### Native bridge

已实现：

```text
UnityPluginLoad / UnityPluginUnload
Q3DC_SetUnityTexture(...)
Q3DC_GetRenderEventFunc()
IUnityGraphicsVulkan.AccessTexture(...)
nativeAttachEncoderSurface(...)
ANativeWindow_fromSurface(...)
Q3DC_GetStatusJson(...)
```

未实现：

```text
Unity VkImage
  -> encoder Surface image
```

## 不再使用的旧判断

旧的解释已经清理：

- Java pattern MP4 只代表 synthetic input smoke，不代表 PCA / Unity texture 输入。
- PCA -> InstantReplay MP4 只证明输入源可用，不代表最终视频输出完成。
- 最终完成标准仍然是 MediaCodec access unit bus 产出真实 Unity/PCA/composite access units。

## 下一阶段唯一主线

不要再扩展 InstantReplay 原型，不要先做 sink consumer，也不要继续把 pattern smoke 当主要验证。

下一步主线：

```text
UnityDynamicTextureEncoderSmokeRunner dynamic RT
  -> native Vulkan bridge
  -> MediaCodec input Surface pixels
  -> H264/H265 access units
  -> MediaMuxer MP4
```

完成标准：

```text
bridgeOutputFrameCount > 0
muxedSampleCount > 0
publishedFramePacketCount > 0
MP4 extracted frame is dynamic Unity test texture, not synthetic Java pattern
```

通过后再切到：

```text
CurrentVideoFrameInputSO.inputTexture
  -> raw PCA
  -> PCA + Unity composite
```

最后再实现：

```text
NetworkFramePacketSender
LocalArtifactStore
NetworkFileArtifactSender
```
