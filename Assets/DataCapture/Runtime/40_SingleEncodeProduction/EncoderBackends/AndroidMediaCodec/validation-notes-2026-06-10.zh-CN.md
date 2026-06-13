# Historical Note: 2026-06-10 Validation

Last cleaned: 2026-06-12

This file used to contain detailed validation notes from the first single-encode smoke tests.
Those details are now folded into the current status documents.

Current source of truth:

```text
README.zh-CN.md
final-architecture-status-validation-2026-06-11.zh-CN.md
```

What still matters from the 2026-06-10 validation:

- PC smoke proved one encoded H264 product can be split into frame packets and remuxed into MP4.
- Android Java smoke proved one `MediaCodec` output path can return access-unit bytes to C# and write the same samples to `MediaMuxer`.
- The Java smoke input was synthetic pattern rendering, not PCA or Unity texture input.

Do not use the old pure-color MP4 as evidence about camera access, PCA texture readiness, or Unity RenderTexture encoding.
