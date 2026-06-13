using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.OfflineReconstruction
{
    public class ControllerPivotReplayRenderer : MonoBehaviour
    {
        [SerializeField] private Transform leftPivot;
        [SerializeField] private Transform rightPivot;

        public void ApplySnapshot(MergedFrameSnapshotRecord snapshot)
        {
            if (!snapshot.hasController)
            {
                return;
            }

            if (leftPivot != null)
            {
                leftPivot.SetPositionAndRotation(
                    snapshot.controller.worldLeftPosition,
                    snapshot.controller.worldLeftRotation);
            }

            if (rightPivot != null)
            {
                rightPivot.SetPositionAndRotation(
                    snapshot.controller.worldRightPosition,
                    snapshot.controller.worldRightRotation);
            }
        }
    }
}
