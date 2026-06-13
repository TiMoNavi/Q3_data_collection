# Legacy 50 Encoding Network SO Assets

This file is a legacy breadcrumb from the old `50_EncodingNetwork` layout. The old mixed
encoding/network assets have been split across:

- `Assets/SOData/DataCapture/40_SingleEncodeProduction`
- `Assets/SOData/DataCapture/50_ProductAssembly`
- `Assets/SOData/DataCapture/60_Distribution`
- `Assets/SOData/DataCapture/90_DebugAndTests`

Keep these assets aligned with the ScriptableObject type definitions under
`Assets/SObasic/Runtime/ScriptableObjects/DataCapture`.

## Usually Edited

- `Encoding/Pipeline/EncodingPipelineConfiguration.asset`
- `Encoding/Source/*` (future frame-source selection assets)
- `Encoding/OutputMode/*` (future mode routing assets)
- `Encoding/DebugImage/DebugImageStreamSettings.asset`
- `Encoding/Video/EncoderConfiguration.asset`
- `Network/Transport/NetworkSenderConfiguration.asset`

## Runtime State

- `Encoding/RuntimeState/CurrentEncodedFrame.asset`
- `Encoding/RuntimeState/EncodedFrameQueue.asset`
- `Network/RuntimeState/CurrentNetworkPacket.asset`
- `Network/RuntimeState/NetworkPacketQueue.asset`

## Discovery And Diagnostics

- `Network/Discovery/PCDiscoveryRequest.asset`
- `Network/Discovery/PCReceiverConnectionStatus.asset`
- `Diagnostics/TransmissionGate/CaptureTransmissionGate.asset`
