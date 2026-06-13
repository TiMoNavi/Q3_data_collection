using UnityEngine;
using DataCapture.Synchronization;

namespace DataCapture
{
    /// <summary>
    /// Captures controller button states and writes them into CurrentControllerPoseSO.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-50)]
    public class ControllerButtonCapture : MonoBehaviour
    {
        [Header("Clock")]
        [SerializeField] private TimeStampVariable timestampVariable;

        [Header("Output")]
        [SerializeField] private CurrentControllerPoseSO currentPose;

        private void Update()
        {
            CaptureNow();
        }

        [ContextMenu("Capture Now")]
        public void CaptureNow()
        {
            if (currentPose == null)
            {
                return;
            }

            ControllerPoseRecord record = currentPose.isValid ? currentPose.ToRecord() : new ControllerPoseRecord();
            record.timestampUnixMs = SynchronizationClock.GetUnixMilliseconds(timestampVariable);

            record.leftTriggerPressed = OVRInput.Get(OVRInput.RawButton.LIndexTrigger);
            record.leftGripPressed = OVRInput.Get(OVRInput.RawButton.LHandTrigger);
            record.leftPrimaryButtonPressed = OVRInput.Get(OVRInput.RawButton.X);
            record.leftSecondaryButtonPressed = OVRInput.Get(OVRInput.RawButton.Y);
            record.rightTriggerPressed = OVRInput.Get(OVRInput.RawButton.RIndexTrigger);
            record.rightGripPressed = OVRInput.Get(OVRInput.RawButton.RHandTrigger);
            record.rightPrimaryButtonPressed = OVRInput.Get(OVRInput.RawButton.A);
            record.rightSecondaryButtonPressed = OVRInput.Get(OVRInput.RawButton.B);

            currentPose.SetPose(record);
        }
    }
}
