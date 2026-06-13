using UnityEngine;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "DistributionGateStateSO", menuName = "DataCapture/60 Distribution/Distribution Gate State")]
    public class DistributionGateStateSO : ScriptableObject, SObasic.IActiveState
    {
        public bool routeReady;
        public bool liveStreamAllowed;
        public bool artifactTransferAllowed;
        public bool localStoreAllowed = true;
        public string activeBlocker = "Distribution route is not ready.";
        public long lastUpdatedUnixMs;

        public bool Active => routeReady && (liveStreamAllowed || artifactTransferAllowed || localStoreAllowed);
    }
}
