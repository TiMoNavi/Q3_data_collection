# Legacy 50 Encoding Network ScriptableObject Types

This file is a legacy breadcrumb from the old `50_EncodingNetwork` layout. The old mixed
encoding/network stage has been split across:

- `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/40_SingleEncodeProduction`
- `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/50_ProductAssembly`
- `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/60_Distribution`
- `Assets/SObasic/Runtime/ScriptableObjects/DataCapture/90_DebugAndTests`

The matching asset instances live under the same phase names in `Assets/SOData/DataCapture`.

## Encoding

- `Encoding/Pipeline`: high-level pipeline mode selection.
- `Encoding/Source`: shared video input source selection and current resolved input texture.
- `Encoding/OutputMode`: DebugLowFpsImage / LocalMp4Save / RealtimeStreamSend routing.
- `Encoding/DebugImage`: low-FPS debug JPEG stream settings.
- `Encoding/Video`: formal video encoder target settings.
- `Encoding/RuntimeState`: latest encoded frame and encoded frame history.

## Network

- `Network/Discovery`: PC receiver discovery and handshake state.
- `Network/Transport`: destination, ports, packet limits, and output target.
- `Network/RuntimeState`: latest sent packet and packet header history.

## Diagnostics

- `Diagnostics/TransmissionGate`: combined gate state for PC readiness, recording state, and synthesis health.
