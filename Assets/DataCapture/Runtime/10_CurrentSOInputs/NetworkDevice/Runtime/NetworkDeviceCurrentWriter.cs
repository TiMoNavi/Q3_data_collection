using UnityEngine;

namespace DataCapture.Synchronization
{
    public class NetworkDeviceCurrentWriter : MonoBehaviour
    {
        [SerializeField] private CurrentNetworkDeviceSO currentDevice;

        public bool Write(NetworkDeviceRecord record)
        {
            if (record.timestampUnixMs <= 0)
            {
                return false;
            }

            if (currentDevice != null)
            {
                currentDevice.SetData(record);
                return true;
            }

            return false;
        }
    }
}
