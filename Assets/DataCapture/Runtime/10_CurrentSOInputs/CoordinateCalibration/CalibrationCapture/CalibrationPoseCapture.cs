using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture
{
    [ExecuteAlways]
    [DefaultExecutionOrder(-45)]
    public class CalibrationPoseCapture : MonoBehaviour
    {
        [Header("Clock")]
        [SerializeField] private TimeStampVariable timestampVariable;

        [Header("Source")]
        [SerializeField] private Transform sourceTransform;
        [SerializeField] private bool useOwnTransformWhenSourceMissing = true;

        [Header("Outputs")]
        [SerializeField] private WorldCoordinateFrameSO worldCoordinateFrame;
        [SerializeField] private SessionCoordinateCalibrationSO sessionCalibration;
        [SerializeField] private bool writeWorldCoordinateFrame = true;
        [SerializeField] private bool writeSessionCalibration = true;
        [SerializeField] private string frameDescription = "Captured calibration point 6DoF.";

        private void Reset()
        {
            sourceTransform = transform;
        }

        private void OnEnable()
        {
            ResolveSourceTransform();
            CaptureNow();
        }

        private void Update()
        {
            CaptureNow();
        }

        [ContextMenu("Capture Now")]
        public void CaptureNow()
        {
            ResolveSourceTransform();

            if (sourceTransform == null)
            {
                return;
            }

            long timestampUnixMs = SynchronizationClock.GetUnixMilliseconds(timestampVariable);
            Vector3 origin = sourceTransform.position;
            Quaternion rotation = sourceTransform.rotation;

            if (writeWorldCoordinateFrame && worldCoordinateFrame != null)
            {
                worldCoordinateFrame.SetFrame(origin, rotation, timestampUnixMs, frameDescription);
            }

            if (writeSessionCalibration && sessionCalibration != null)
            {
                sessionCalibration.SetFrame(origin, rotation, timestampUnixMs, frameDescription);
            }
        }

        private void ResolveSourceTransform()
        {
            if (sourceTransform == null && useOwnTransformWhenSourceMissing)
            {
                sourceTransform = transform;
            }
        }
    }
}
