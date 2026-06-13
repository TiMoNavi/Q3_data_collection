using UnityEngine;

namespace DataCapture.Synchronization
{
    public class QueueMemoryValidator : MonoBehaviour
    {
        [SerializeField] private CameraFrameTimingQueueSO cameraTimingQueue;
        [SerializeField] private int expectedMaxCapacity = 300;

        [ContextMenu("Validate Camera Queue Capacity")]
        public bool ValidateCameraQueueCapacity()
        {
            if (cameraTimingQueue == null)
            {
                Debug.LogWarning("Camera timing queue is not assigned.", this);
                return false;
            }

            bool valid = cameraTimingQueue.Capacity <= expectedMaxCapacity;
            if (!valid)
            {
                Debug.LogWarning("Camera timing queue capacity exceeds expected max: " + cameraTimingQueue.Capacity, this);
            }

            return valid;
        }
    }
}
