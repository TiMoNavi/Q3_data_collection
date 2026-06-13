using DataCapture.Networking;
using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Testing
{
    // Runs before live controller writers so debugger/AI SO edits can be observed before capture overwrites them.
    [DefaultExecutionOrder(-56)]
    public sealed class ControllerButtonDiscoveryRequestListener : MonoBehaviour
    {
        [SerializeField] private CurrentControllerPoseSO currentControllerPose;
        [SerializeField] private PCDiscoveryRequestSO discoveryRequest;
        [SerializeField] private ControllerRecordingButtonBinding discoveryButton = ControllerRecordingButtonBinding.LeftPrimaryButton;
        [SerializeField] private bool requestOnlyOnPressDown = true;

        private bool wasPressed;
        private string requestSource;

        private void OnEnable()
        {
            RefreshRequestSource();
        }

        private void OnValidate()
        {
            RefreshRequestSource();
        }

        private void Update()
        {
            if (currentControllerPose == null || discoveryRequest == null)
            {
                return;
            }

            bool pressed = IsButtonPressed(currentControllerPose);
            if (pressed && (!requestOnlyOnPressDown || !wasPressed))
            {
                discoveryRequest.Request(requestSource);
            }

            wasPressed = pressed;
        }

        private bool IsButtonPressed(CurrentControllerPoseSO pose)
        {
            switch (discoveryButton)
            {
                case ControllerRecordingButtonBinding.LeftTrigger:
                    return pose.leftTriggerPressed;
                case ControllerRecordingButtonBinding.LeftGrip:
                    return pose.leftGripPressed;
                case ControllerRecordingButtonBinding.LeftPrimaryButton:
                    return pose.leftPrimaryButtonPressed;
                case ControllerRecordingButtonBinding.LeftSecondaryButton:
                    return pose.leftSecondaryButtonPressed;
                case ControllerRecordingButtonBinding.RightTrigger:
                    return pose.rightTriggerPressed;
                case ControllerRecordingButtonBinding.RightGrip:
                    return pose.rightGripPressed;
                case ControllerRecordingButtonBinding.RightPrimaryButton:
                    return pose.rightPrimaryButtonPressed;
                case ControllerRecordingButtonBinding.RightSecondaryButton:
                    return pose.rightSecondaryButtonPressed;
                default:
                    return false;
            }
        }

        private void RefreshRequestSource()
        {
            requestSource = GetRequestSource(discoveryButton);
        }

        private static string GetRequestSource(ControllerRecordingButtonBinding button)
        {
            switch (button)
            {
                case ControllerRecordingButtonBinding.LeftTrigger:
                    return "Controller LeftTrigger";
                case ControllerRecordingButtonBinding.LeftGrip:
                    return "Controller LeftGrip";
                case ControllerRecordingButtonBinding.LeftPrimaryButton:
                    return "Controller LeftPrimaryButton";
                case ControllerRecordingButtonBinding.LeftSecondaryButton:
                    return "Controller LeftSecondaryButton";
                case ControllerRecordingButtonBinding.RightTrigger:
                    return "Controller RightTrigger";
                case ControllerRecordingButtonBinding.RightGrip:
                    return "Controller RightGrip";
                case ControllerRecordingButtonBinding.RightPrimaryButton:
                    return "Controller RightPrimaryButton";
                case ControllerRecordingButtonBinding.RightSecondaryButton:
                    return "Controller RightSecondaryButton";
                default:
                    return "Controller Unknown";
            }
        }
    }
}
