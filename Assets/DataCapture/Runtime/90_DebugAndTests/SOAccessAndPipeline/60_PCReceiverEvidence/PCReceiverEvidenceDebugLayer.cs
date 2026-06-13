// SO debug layer placeholder: 60_PCReceiverEvidence.
//
// Purpose:
// - Represent the boundary between Unity-side evidence and external PC receiver
//   evidence.
// - Unity can prove packet generation / send intent. PC file output must be
//   verified by the receiver process or an external script and then reported
//   back through a simple generic channel if needed.
//
// SO assets to bind from Assets/SOData:
// - DataCapture/50_EncodingNetwork/Network/RuntimeState/NetworkPacketQueue.asset
//   Unity-side final evidence: latest packet headers.
// - DataCapture/50_EncodingNetwork/Network/RuntimeState/CurrentNetworkPacket.asset
//   Unity-side latest packet evidence.
// - DataCapture/50_EncodingNetwork/Encoding/RuntimeState/CurrentEncodedFrame.asset
//   Expected codec / frame id / byte length for receiver-side matching.
// - Optional future simple summary SO:
//   If the PC receiver needs to write back result text, use one generic summary
//   SO or a generic SOFieldWriteRequest target. Do not create one result SO per
//   codec / test case.
//
// Normal conditions:
// - Unity side has produced the expected packet.
// - External PC receiver logs or files contain matching frame id / codec /
//   timestamp evidence.
// - If external evidence is written back into Unity, the Debug log must state
//   the receiver path and exact external result fields.
//
// Advancement actions:
// - No Unity-side advancement is required after packet output.
// - Optional external script may write a generic result field through
//   SOFieldWriteRequest / SOValueAccessUtility, then Unity Debug logs it.
//
// Stop conditions:
// - Unity produced packets but PC receiver has no matching file/log evidence.
// - External receiver reports codec mismatch, missing metadata, missing image,
//   missing video frame, or malformed payload.
//
// Unity Debug output must include:
// - [SO-Debug][PASS][PCReceiverEvidence] expected codec, expected frame id,
//   receiver evidence path, external result text.
// - [SO-Debug][FAIL][PCReceiverEvidence] last Unity packet fields plus the
//   missing external evidence condition.
//
// Future implementation shape:
// - Keep this layer thin. It is a boundary adapter, not a second state machine.
// - Prefer one generic external evidence payload over many test-specific SOs.
