using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Networking
{
    public enum ControllerRecordingButtonBinding
    {
        LeftTrigger,
        LeftGrip,
        LeftPrimaryButton,
        LeftSecondaryButton,
        RightTrigger,
        RightGrip,
        RightPrimaryButton,
        RightSecondaryButton
    }

    // Runs before live controller writers so debugger/AI SO edits can be observed before capture overwrites them.
    [DefaultExecutionOrder(-55)]
    public class ControllerButtonRecordingToggleListener : MonoBehaviour
    {
        [SerializeField] private CurrentControllerPoseSO currentControllerPose;
        [SerializeField] private RecordingToggleRequestSO toggleRequest;
        [SerializeField] private ControllerRecordingButtonBinding recordingButton = ControllerRecordingButtonBinding.LeftSecondaryButton;
        [SerializeField] private bool requestOnlyOnPressDown = true;

        private bool wasPressed;

        private void Update()
        {
            if (currentControllerPose == null || toggleRequest == null)
            {
                return;
            }

            bool pressed = IsButtonPressed(currentControllerPose);
            if (pressed && (!requestOnlyOnPressDown || !wasPressed))
            {
                toggleRequest.Request("Controller " + recordingButton);
            }

            wasPressed = pressed;
        }

        private bool IsButtonPressed(CurrentControllerPoseSO pose)
        {
            switch (recordingButton)
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
    }
}
