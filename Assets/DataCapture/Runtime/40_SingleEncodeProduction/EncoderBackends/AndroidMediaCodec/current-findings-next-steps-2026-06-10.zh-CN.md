# Historical Note: 2026-06-10 Findings

Last cleaned: 2026-06-12

This file used to contain the 2026-06-10 working notes for `SingleEncodeStreamAndMp4`.
Those notes are now superseded.

Current source of truth:

```text
README.zh-CN.md
final-architecture-status-validation-2026-06-11.zh-CN.md
```

Important corrections after this file was originally written:

- The final goal is not `PCA -> MP4`; it is `PCA / composite RenderTexture -> one MediaCodec encode -> one H264/H265 access unit bus -> multiple sinks`.
- `SingleEncodeAndroidMuxerSmoke_20260610_193429_capture.mp4` was synthetic Java pattern output from `encodePatternFrame(...)`.
- That pure-color MP4 proves MediaCodec + MediaMuxer plumbing, not PCA texture input.
- Real PCA -> staging RenderTexture -> InstantReplay MP4 was later validated on Quest.
- Unity RenderTexture -> Vulkan `AccessTexture` and MediaCodec input Surface -> `ANativeWindow` were later validated on Quest.
- The remaining blocker is Unity `VkImage` -> MediaCodec input Surface pixel transfer.

Keep this file only as a historical pointer.
