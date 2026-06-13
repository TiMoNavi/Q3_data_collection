using System;

namespace DataCapture.Synchronization
{
    public enum MergedFrameStatus
    {
        Complete,
        PartialDebugOnly,
        WaitingForCamera,
        WaitingForRequiredSources,
        DroppedMissingRequired,
        DroppedSourceOffline,
        DroppedQueueOverflow,
        DroppedBadTimestamp
    }

    [Flags]
    public enum MergedFrameStreamMask
    {
        None = 0,
        CameraTiming = 1 << 0,
        CameraImage = 1 << 1,
        CameraPose = 1 << 2,
        CameraMetadata = 1 << 3,
        CameraStreamState = 1 << 4,
        Controller = 1 << 5,
        VirtualLayer = 1 << 6
    }

    [System.Serializable]
    public struct MergedFrameSnapshotRecord : ITimestampedData
    {
        public long frameId;
        public long timestampUnixMs;
        public MergedFrameStatus status;
        public bool isSendable;
        public string dropReason;
        public MergedFrameStreamMask requiredStreamMask;
        public MergedFrameStreamMask missingRequiredStreamMask;
        public MergedFrameStreamMask matchedStreamMask;
        public CameraFrameTimingRecord cameraTiming;
        public CameraImageFrameRecord cameraImage;
        public CameraPoseRecord cameraPose;
        public CameraMetadataRecord cameraMetadata;
        public CameraStreamStateRecord cameraStreamState;
        public ControllerPoseRecord controller;
        public VirtualLayerFrameRecord virtualLayer;
        public bool hasCameraImage;
        public bool hasCameraPose;
        public bool hasCameraMetadata;
        public bool hasCameraStreamState;
        public bool hasController;
        public bool hasVirtualLayer;
        public long cameraImageTimeDeltaMs;
        public long cameraPoseTimeDeltaMs;
        public long cameraMetadataTimeDeltaMs;
        public long cameraStreamStateTimeDeltaMs;
        public long controllerTimeDeltaMs;
        public long virtualLayerTimeDeltaMs;
        public FrameMatchStatus cameraImageMatchStatus;
        public FrameMatchStatus cameraPoseMatchStatus;
        public FrameMatchStatus cameraMetadataMatchStatus;
        public FrameMatchStatus cameraStreamStateMatchStatus;
        public FrameMatchStatus controllerMatchStatus;
        public FrameMatchStatus virtualLayerMatchStatus;
        public string cameraImageDropReason;
        public string cameraPoseDropReason;
        public string cameraMetadataDropReason;
        public string cameraStreamStateDropReason;
        public string controllerDropReason;
        public string virtualLayerDropReason;

        public long Timestamp => timestampUnixMs;
    }
}
