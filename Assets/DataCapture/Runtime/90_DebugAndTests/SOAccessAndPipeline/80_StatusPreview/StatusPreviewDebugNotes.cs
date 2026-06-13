// SO debug layer placeholder: 80_StatusPreview.
//
// Purpose:
// - Document the relationship between runtime status preview UI and SO-driven
//   diagnostics.
// - Status preview should read the same production SO fields as the debug
//   pipeline. It should not own independent truth about chain health.
//
// SO assets to observe from Assets/SOData:
// - PCReceiverConnectionStatus, RecordingSessionState, Current* SOs,
//   required queues, TimestampMergerDebugState, CaptureTransmissionGate,
//   EncodingPipelineConfiguration, CurrentEncodedFrame, CurrentNetworkPacket,
//   NetworkPacketQueue.
//
// Normal conditions:
// - Preview panels summarize the same pass/fail fields emitted through
//   Unity Debug.
// - A visual status item must map back to an SO asset name and field path.
//
// Advancement actions:
// - None. Preview is read-only.
//
// Stop conditions:
// - If preview shows a state that disagrees with the SO debug logs, trust the
//   SO field dump first and fix the preview binding.
//
// Unity Debug output must include:
// - No extra status-preview-only log format is required. Reuse the integrated
//   [SO-Debug] layer logs so ADB Logcat and Unity Console show the same truth.
//
// Future implementation shape:
// - Optional: a small UI adapter that consumes the integrated pipeline's latest
//   layer result after the pipeline implementation exists.
