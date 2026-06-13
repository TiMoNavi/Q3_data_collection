using UnityEngine;
using UnityEngine.Android;

namespace DataCapture.ImageTraining
{
    /// <summary>
    /// Requests HEADSET_CAMERA permission for Quest camera access.
    /// </summary>
    public class CameraPermissionRequest : MonoBehaviour
    {
        private const string CAMERA_PERMISSION = "horizonos.permission.HEADSET_CAMERA";

        void Start()
        {
            if (!Permission.HasUserAuthorizedPermission(CAMERA_PERMISSION))
            {
                Permission.RequestUserPermission(CAMERA_PERMISSION);
            }
        }
    }
}
