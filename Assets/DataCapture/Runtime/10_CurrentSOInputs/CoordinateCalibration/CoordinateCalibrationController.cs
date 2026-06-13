using SObasic;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Synchronization
{
    public class CoordinateCalibrationController : MonoBehaviour
    {
        [Header("Clock")]
        [SerializeField] private TimeStampVariable timestampVariable;

        [Header("Trigger")]
        [SerializeField] private BoolVariable resetRequest;
        [SerializeField] private bool clearRequestAfterHandling = true;

        [Header("Recording Flow Triggers")]
        [SerializeField] private RecordingSessionStateSO recordingState;
        [SerializeField] private bool calibrateWhenRecordingStateEntersWarmup = true;

        [Header("Sources")]
        [SerializeField] private Transform centerEyeAnchor;
        [SerializeField] private bool autoFindCenterEyeAnchor = true;
        [SerializeField] private float floorWorldY = 0f;

        [Header("Coordinate Frames")]
        [SerializeField] private WorldCoordinateFrameSO worldCoordinateFrame;
        [SerializeField] private SessionCoordinateCalibrationSO sessionCalibration;
        [SerializeField] private bool initializeWorldFrameOnEnable = true;

        private bool lastRequestActive;
        private RecordingSessionState lastRecordingState = RecordingSessionState.NotStarted;

        private void OnEnable()
        {
            lastRequestActive = resetRequest != null && resetRequest.Active;
            lastRecordingState = recordingState != null
                ? recordingState.State
                : RecordingSessionState.NotStarted;

            if (initializeWorldFrameOnEnable && worldCoordinateFrame != null)
            {
                worldCoordinateFrame.SetIdentity(SynchronizationClock.GetUnixMilliseconds(timestampVariable));
            }
        }

        private void Update()
        {
            UpdateResetRequestTrigger();
            UpdateRecordingStateTrigger();
        }

        private void UpdateResetRequestTrigger()
        {
            if (resetRequest == null)
            {
                return;
            }

            bool requestActive = resetRequest.Active;
            if (requestActive && !lastRequestActive)
            {
                CalibrateNow();

                if (clearRequestAfterHandling)
                {
                    resetRequest.Value = false;
                    requestActive = false;
                }
            }

            lastRequestActive = requestActive;
        }

        private void UpdateRecordingStateTrigger()
        {
            if (recordingState == null)
            {
                return;
            }

            RecordingSessionState activeState = recordingState.State;
            if (activeState != lastRecordingState &&
                activeState == RecordingSessionState.WarmingUp &&
                calibrateWhenRecordingStateEntersWarmup)
            {
                CalibrateNow();
            }

            lastRecordingState = activeState;
        }

        [ContextMenu("Calibrate Now")]
        public bool CalibrateNow()
        {
            ResolveCenterEyeAnchor();

            if (worldCoordinateFrame != null)
            {
                worldCoordinateFrame.SetIdentity(SynchronizationClock.GetUnixMilliseconds(timestampVariable));
            }

            if (centerEyeAnchor == null || sessionCalibration == null)
            {
                Debug.LogWarning(
                    "CoordinateCalibrationController requires a CenterEyeAnchor and SessionCoordinateCalibrationSO.",
                    this);
                return false;
            }

            bool calibrated = sessionCalibration.CalibrateFromCenterEye(
                centerEyeAnchor,
                floorWorldY,
                SynchronizationClock.GetUnixMilliseconds(timestampVariable));

            if (!calibrated)
            {
                Debug.LogWarning(
                    "Coordinate calibration failed because the center eye forward direction could not define a horizontal +Z.",
                    this);
            }

            return calibrated;
        }

        private void ResolveCenterEyeAnchor()
        {
            if (centerEyeAnchor != null || !autoFindCenterEyeAnchor)
            {
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                centerEyeAnchor = mainCamera.transform;
                return;
            }

            GameObject centerEyeObject = GameObject.Find("CenterEyeAnchor");
            if (centerEyeObject != null)
            {
                centerEyeAnchor = centerEyeObject.transform;
            }
        }

    }
}
