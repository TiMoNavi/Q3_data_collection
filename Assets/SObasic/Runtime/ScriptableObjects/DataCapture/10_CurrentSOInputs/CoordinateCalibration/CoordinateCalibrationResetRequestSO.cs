using SObasic;
using UnityEngine;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(
        fileName = "CoordinateCalibrationResetRequest",
        menuName = "DataCapture/Coordinate System Calibration/Reset Request")]
    public class CoordinateCalibrationResetRequestSO : BoolVariable
    {
        public void RequestReset()
        {
            Value = true;
        }

        public void ClearRequest()
        {
            Value = false;
        }
    }
}
