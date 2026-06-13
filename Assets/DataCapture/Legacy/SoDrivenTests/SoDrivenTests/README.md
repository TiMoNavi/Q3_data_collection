# Legacy SO-Driven Tests

These files are preserved for reference only.

They belong to the older per-test SO runner approach:

- `DataCaptureSoDrivenAutoRecordingTest`
- `DataCaptureSoDrivenEncodingSwitchTest`

Do not extend this folder for the current debug chain. New runtime diagnostics should use the SO debug probe design in:

```text
Assets/docs/10-so-debug-layer-design.md
```

The generic SO write bridge remains active in:

```text
Assets/DataCapture/Runtime/90_DebugAndTests/SOAccessAndPipeline
```
