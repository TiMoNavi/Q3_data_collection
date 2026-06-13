using System;
using System.Collections;
using System.Collections.Generic;
using DataCapture.Networking;
using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Testing
{
    [Serializable]
    public sealed class HandshakeRecordingControlDebugLayer
    {
        // Layer resources:
        // - Input SO for discovery simulation: CurrentControllerPose.leftPrimaryButtonPressed.
        // - Listener: ControllerButtonDiscoveryRequestListener turns that button into PCDiscoveryRequestSO.Request().
        // - Handshake state: PCReceiverConnectionStatusSO and NetworkSenderConfigurationSO.
        // - Input SO for recording simulation: CurrentControllerPose.leftSecondaryButtonPressed.
        // - Existing listener: ControllerButtonRecordingToggleListener turns that button into RecordingToggleRequestSO.Request().
        // - Recording state: RecordingSessionStateSO.
        //
        // Normal:
        // - PCReceiverConnectionStatusSO.CanStartRecording == true.
        // - RecordingSessionStateSO.ShouldWriteQueues == true and HasException == false.
        //
        // Advancement:
        // - Simulate LeftPrimaryButton for discovery.
        // - Simulate LeftSecondaryButton for recording start/stop.
        //
        // Stop:
        // - No LAN targets, socket error, no PC response, incompatible response, recording exception.

        [Header("Handshake SOs")]
        [SerializeField] private PCDiscoveryRequestSO discoveryRequest;
        [SerializeField] private PCReceiverConnectionStatusSO pcReceiverStatus;
        [SerializeField] private NetworkSenderConfigurationSO networkConfiguration;

        [Header("Recording SOs")]
        [SerializeField] private RecordingToggleRequestSO recordingToggleRequest;
        [SerializeField] private RecordingSessionStateSO recordingState;

        [Header("Controller Button SO Input")]
        [SerializeField] private CurrentControllerPoseSO currentControllerPose;
        [SerializeField] private ControllerRecordingButtonBinding discoveryButton = ControllerRecordingButtonBinding.LeftPrimaryButton;
        [SerializeField] private ControllerRecordingButtonBinding recordingToggleButton = ControllerRecordingButtonBinding.LeftSecondaryButton;
        [SerializeField] private float simulatedButtonHoldSeconds = 0.2f;

        private bool recordingStartRequestedByLayer;
        private float recordingStartRequestedAt;

        public void ResetRunState()
        {
            recordingStartRequestedByLayer = false;
            recordingStartRequestedAt = -1f;
        }

        public IEnumerator NormalizeRecordingBeforeRun(
            MonoBehaviour owner,
            float stopTimeoutSeconds,
            float pollIntervalSeconds,
            Action<bool> complete)
        {
            if (!IsRecordingActive())
            {
                complete(true);
                yield break;
            }

            yield return SimulateRecordingButton(owner, "NormalizeStopBeforeRun");
            yield return WaitForRecordingInactive(stopTimeoutSeconds, pollIntervalSeconds);

            if (IsRecordingActive())
            {
                SODebugLog.Fail(
                    owner,
                    "RecordingNormalize",
                    "RecordingSessionState",
                    "Active==false before debug run",
                    "Active=True",
                    "An existing automatic recording path is still active. Disable the old auto trigger before running this SO debug pipeline.",
                    stopTimeoutSeconds,
                    BuildRecordingFields());
                complete(false);
                yield break;
            }

            complete(true);
        }

        public IEnumerator RunHandshake(MonoBehaviour owner, float timeoutSeconds, float pollIntervalSeconds, Action<bool> complete)
        {
            if (pcReceiverStatus == null)
            {
                SODebugLog.Fail(owner, "Handshake", "PCReceiverConnectionStatus", "assigned", "null", "Missing SO reference.", 0f);
                complete(false);
                yield break;
            }

            if (pcReceiverStatus.CanStartRecording)
            {
                SODebugLog.Pass(owner, "Handshake", BuildHandshakeFields("alreadyPaired=True"));
                complete(true);
                yield break;
            }

            if (discoveryRequest == null)
            {
                SODebugLog.Fail(owner, "Handshake", "PCDiscoveryRequest", "assigned", "null", "Cannot observe LAN discovery request.", 0f);
                complete(false);
                yield break;
            }

            if (currentControllerPose == null)
            {
                SODebugLog.Fail(owner, "Handshake", "CurrentControllerPose", "assigned", "null", "Cannot simulate controller button input for PC discovery.", 0f);
                complete(false);
                yield break;
            }

            int revisionBefore = discoveryRequest.requestRevision;
            yield return SODebugControllerButtons.Press(
                owner,
                currentControllerPose,
                discoveryButton,
                simulatedButtonHoldSeconds,
                "Handshake",
                "PCDiscoveryButton",
                SODebugLog.Fields(
                    "expectedRequest=PCDiscoveryRequest",
                    "requestRevisionBefore=" + revisionBefore));

            if (discoveryRequest.requestRevision <= revisionBefore)
            {
                SODebugLog.Fail(
                    owner,
                    "Handshake",
                    "PCDiscoveryRequest",
                    "requestRevision > requestRevisionBefore after controller button SO write",
                    "requestRevision=" + discoveryRequest.requestRevision + ", requestRevisionBefore=" + revisionBefore,
                    "CurrentControllerPose button write did not reach PCDiscoveryRequestSO. Check ControllerButtonDiscoveryRequestListener, discoveryButton binding, and SO references on SO_Debug_Probe.",
                    0f,
                    BuildHandshakeFields("button=" + discoveryButton));
                complete(false);
                yield break;
            }

            float deadline = Time.unscaledTime + Mathf.Max(0.1f, timeoutSeconds);
            while (Time.unscaledTime < deadline)
            {
                if (pcReceiverStatus.CanStartRecording)
                {
                    SODebugLog.Pass(owner, "Handshake", BuildHandshakeFields("alreadyPaired=False"));
                    complete(true);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, pollIntervalSeconds));
            }

            SODebugLog.Fail(
                owner,
                "Handshake",
                "PCReceiverConnectionStatus",
                "CanStartRecording==true",
                "CanStartRecording=False",
                BuildHandshakeBlocker(),
                timeoutSeconds,
                BuildHandshakeFields());
            complete(false);
        }

        public IEnumerator RunRecordingStart(MonoBehaviour owner, float timeoutSeconds, Action<bool> complete)
        {
            if (recordingToggleRequest == null)
            {
                SODebugLog.Fail(owner, "Recording", "RecordingToggleRequest", "assigned", "null", "Cannot observe recording toggle request.", 0f);
                complete(false);
                yield break;
            }

            if (recordingState == null)
            {
                SODebugLog.Fail(owner, "Recording", "RecordingSessionState", "assigned", "null", "Cannot observe recording state.", 0f);
                complete(false);
                yield break;
            }

            if (!recordingState.ShouldWriteQueues)
            {
                int revisionBefore = recordingToggleRequest.requestRevision;
                yield return SimulateRecordingButton(owner, "StartRecording");
                if (recordingToggleRequest.requestRevision <= revisionBefore && !recordingState.ShouldWriteQueues)
                {
                    SODebugLog.Fail(
                        owner,
                        "Recording",
                        "RecordingToggleRequest",
                        "requestRevision > requestRevisionBefore after controller button SO write",
                        "requestRevision=" + recordingToggleRequest.requestRevision + ", requestRevisionBefore=" + revisionBefore,
                        "CurrentControllerPose button write did not reach RecordingToggleRequestSO. Check ControllerButtonRecordingToggleListener, recordingToggleButton binding, and SO references in the scene.",
                        0f,
                        BuildRecordingFields("button=" + recordingToggleButton));
                    complete(false);
                    yield break;
                }
            }

            float deadline = Time.unscaledTime + Mathf.Max(0.1f, timeoutSeconds);
            while (Time.unscaledTime < deadline)
            {
                if (recordingState.ShouldWriteQueues && !recordingState.HasException)
                {
                    SODebugLog.Pass(owner, "Recording", BuildRecordingFields());
                    complete(true);
                    yield break;
                }

                if (recordingState.HasException)
                {
                    break;
                }

                yield return null;
            }

            SODebugLog.Fail(
                owner,
                "Recording",
                "RecordingSessionState",
                "ShouldWriteQueues==true && HasException==false",
                "ShouldWriteQueues=" + SODebugLog.Bool(recordingState.ShouldWriteQueues) + ", HasException=" + SODebugLog.Bool(recordingState.HasException),
                recordingState.HasException ? recordingState.LastExceptionReason : "Recording button simulation did not advance the official recording state.",
                timeoutSeconds,
                BuildRecordingFields());
            complete(false);
        }

        public IEnumerator StopRecordingWindowIfNeeded(
            MonoBehaviour owner,
            bool stopRecordingAtEnd,
            float recordingDurationSeconds,
            float stopTimeoutSeconds,
            float pollIntervalSeconds)
        {
            if (!stopRecordingAtEnd || !recordingStartRequestedByLayer)
            {
                yield break;
            }

            if (!IsRecordingActive())
            {
                SODebugLog.Pass(owner, "RecordingStop", BuildRecordingFields("alreadyInactive=True"));
                yield break;
            }

            float stopAt = recordingStartRequestedAt + Mathf.Max(0f, recordingDurationSeconds);
            while (Time.unscaledTime < stopAt)
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, pollIntervalSeconds));
            }

            yield return SimulateRecordingButton(owner, "StopRecordingAfterWindow");
            yield return WaitForRecordingInactive(stopTimeoutSeconds, pollIntervalSeconds);

            if (IsRecordingActive())
            {
                SODebugLog.Fail(
                    owner,
                    "RecordingStop",
                    "RecordingSessionState",
                    "Active==false",
                    "Active=True",
                    "Recording did not stop after simulated stop button.",
                    stopTimeoutSeconds,
                    BuildRecordingFields());
            }
            else
            {
                SODebugLog.Pass(owner, "RecordingStop", BuildRecordingFields("alreadyInactive=False"));
            }
        }

        private IEnumerator SimulateRecordingButton(MonoBehaviour owner, string reason)
        {
            if (reason == "StartRecording" && currentControllerPose != null)
            {
                recordingStartRequestedByLayer = true;
                recordingStartRequestedAt = Time.unscaledTime;
            }

            yield return SODebugControllerButtons.Press(
                owner,
                currentControllerPose,
                recordingToggleButton,
                simulatedButtonHoldSeconds,
                "Recording",
                "RecordingToggle." + reason,
                SODebugLog.Fields(
                    "expectedRequest=RecordingToggleRequest",
                    "requestRevisionBefore=" + (recordingToggleRequest != null ? recordingToggleRequest.requestRevision.ToString() : "null")));
        }

        private IEnumerator WaitForRecordingInactive(float timeoutSeconds, float pollIntervalSeconds)
        {
            float deadline = Time.unscaledTime + Mathf.Max(0.1f, timeoutSeconds);
            while (Time.unscaledTime < deadline && IsRecordingActive())
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, pollIntervalSeconds));
            }
        }

        private bool IsRecordingActive()
        {
            return recordingState != null && recordingState.Active;
        }

        private string BuildHandshakeFields(params string[] extra)
        {
            var parts = new List<string>
            {
                "phase=" + (pcReceiverStatus != null ? pcReceiverStatus.phase.ToString() : "null"),
                "handshakeSucceeded=" + (pcReceiverStatus != null ? SODebugLog.Bool(pcReceiverStatus.handshakeSucceeded) : "null"),
                "CanStartRecording=" + (pcReceiverStatus != null ? SODebugLog.Bool(pcReceiverStatus.CanStartRecording) : "null"),
                "remoteHost=" + SODebugLog.Empty(pcReceiverStatus != null ? pcReceiverStatus.remoteHost : null),
                "metadataPort=" + (pcReceiverStatus != null ? pcReceiverStatus.metadataPort.ToString() : "null"),
                "videoPort=" + (pcReceiverStatus != null ? pcReceiverStatus.videoPort.ToString() : "null"),
                "discoveryAttemptCount=" + (pcReceiverStatus != null ? pcReceiverStatus.discoveryAttemptCount.ToString() : "null"),
                "discoveryRequestRevision=" + (discoveryRequest != null ? discoveryRequest.requestRevision.ToString() : "null"),
                "discoveryRequestSource=" + SODebugLog.Empty(discoveryRequest != null ? discoveryRequest.requestSource : null),
                "socketErrorCount=" + (pcReceiverStatus != null ? pcReceiverStatus.socketErrorCount.ToString() : "null"),
                "lastBlocker=" + SODebugLog.Empty(pcReceiverStatus != null ? pcReceiverStatus.lastBlocker : null),
                "lastErrorMessage=" + SODebugLog.Empty(pcReceiverStatus != null ? pcReceiverStatus.lastErrorMessage : null),
                "lastDiscoveryTargets=" + SODebugLog.Empty(pcReceiverStatus != null ? pcReceiverStatus.lastDiscoveryTargets : null),
                "networkWarning=" + SODebugLog.Empty(pcReceiverStatus != null ? pcReceiverStatus.networkWarning : null),
                "localNetworkSummary=" + SODebugLog.Empty(pcReceiverStatus != null ? pcReceiverStatus.localNetworkSummary : null),
                "vpnOrTunnelInterfaceDetected=" + (pcReceiverStatus != null ? SODebugLog.Bool(pcReceiverStatus.vpnOrTunnelInterfaceDetected) : "null"),
                "discoveryCandidateInterfaceCount=" + (pcReceiverStatus != null ? pcReceiverStatus.discoveryCandidateInterfaceCount.ToString() : "null"),
                "ignoredVpnInterfaceCount=" + (pcReceiverStatus != null ? pcReceiverStatus.ignoredVpnInterfaceCount.ToString() : "null"),
                "discoveryTargetCount=" + (pcReceiverStatus != null ? pcReceiverStatus.discoveryTargetCount.ToString() : "null"),
                "enableLanDiscovery=" + (networkConfiguration != null ? SODebugLog.Bool(networkConfiguration.enableLanDiscovery) : "null"),
                "configuredDiscoveryPort=" + (networkConfiguration != null ? networkConfiguration.discoveryPort.ToString() : "null")
            };

            parts.AddRange(extra);
            return string.Join("; ", parts);
        }

        private string BuildHandshakeBlocker()
        {
            if (networkConfiguration == null)
            {
                return "NetworkSenderConfigurationSO is missing, so LAN discovery cannot be evaluated.";
            }

            if (!networkConfiguration.enableLanDiscovery)
            {
                return "LAN discovery is disabled in NetworkSenderConfigurationSO.";
            }

            if (pcReceiverStatus.discoveryCandidateInterfaceCount == 0 || pcReceiverStatus.discoveryTargetCount == 0)
            {
                return "No LAN discovery targets were available. The headset may not be connected to Wi-Fi, no usable LAN interface was found, or all interfaces were filtered as VPN/tunnel.";
            }

            if (pcReceiverStatus.phase == PCReceiverConnectionPhase.SocketError || pcReceiverStatus.socketErrorCount > 0)
            {
                return "Discovery socket failed. Check Wi-Fi state, local network permissions, and UDP discovery port " + networkConfiguration.discoveryPort + ".";
            }

            if (pcReceiverStatus.phase == PCReceiverConnectionPhase.MalformedResponse)
            {
                return "A PC response was received but was not valid Q3DC discovery JSON. Check receiver version and protocol.";
            }

            if (pcReceiverStatus.phase == PCReceiverConnectionPhase.IncompatibleResponse)
            {
                return "A PC response was received but protocol/magic did not match. Check PC receiver version.";
            }

            if (pcReceiverStatus.discoveryAttemptCount > 0)
            {
                return "Discovery probes were sent but no valid PC response arrived. The PC receiver may not be running, Quest and PC may be on different networks, or a firewall may be blocking UDP discovery/response traffic.";
            }

            return string.IsNullOrWhiteSpace(pcReceiverStatus.lastBlocker)
                ? "PC receiver is not ready."
                : pcReceiverStatus.lastBlocker;
        }

        private string BuildRecordingFields(params string[] extra)
        {
            var parts = new List<string>
            {
                "State=" + (recordingState != null ? recordingState.State.ToString() : "null"),
                "Active=" + (recordingState != null ? SODebugLog.Bool(recordingState.Active) : "null"),
                "IsRecording=" + (recordingState != null ? SODebugLog.Bool(recordingState.IsRecording) : "null"),
                "ShouldWriteQueues=" + (recordingState != null ? SODebugLog.Bool(recordingState.ShouldWriteQueues) : "null"),
                "HasException=" + (recordingState != null ? SODebugLog.Bool(recordingState.HasException) : "null"),
                "LastExceptionReason=" + SODebugLog.Empty(recordingState != null ? recordingState.LastExceptionReason : null),
                "LastDebugMessage=" + SODebugLog.Empty(recordingState != null ? recordingState.LastDebugMessage : null),
                "requestRevision=" + (recordingToggleRequest != null ? recordingToggleRequest.requestRevision.ToString() : "null"),
                "requestSource=" + SODebugLog.Empty(recordingToggleRequest != null ? recordingToggleRequest.requestSource : null)
            };

            parts.AddRange(extra);
            return string.Join("; ", parts);
        }
    }
}
