# Scene And Data Flow

Last updated: 2026-06-12

## Build Scene Flow

```mermaid
graph LR
    Launch[Android Player Launch] --> SampleScene[Assets/Scenes/SampleScene.unity]
```

Only `SampleScene.unity` is enabled in `ProjectSettings/EditorBuildSettings.asset`.

Other scenes found in `Assets/Scenes`:

- `SingleEncodePcSmoke.unity`: smoke/prototype scene for single-encode and bridge validation. Not in Build Settings.
- `New Scene.unity`: not in Build Settings.

## Main Data Flow

```mermaid
flowchart TD
    subgraph QuestRuntime[Quest Runtime]
        CameraRig[OVRCameraRig]
        PCA[PassthroughCameraAccess]
        Controller[Controller Anchors / Buttons]
        Headset[CenterEyeAnchor]
    end

    subgraph CurrentLayer[10_CurrentSOInputs]
        CameraWriter[PassthroughCameraFrameWriter]
        VirtualWriter[VirtualLayerFrameWriter]
        ControllerWriter[ControllerPoseCapture / ButtonCapture]
        HeadsetWriter[HeadsetPoseCapture]
        CurrentSOs[Current* ScriptableObjects]
    end

    subgraph QueueLayer[20_QueueBuffers]
        RecordingState[RecordingSessionStateSO]
        Recorders[CurrentToQueueRecorder]
        QueueSOs[*Queue ScriptableObjects]
    end

    subgraph SyncLayer[30_TimeSynchronization]
        Merger[TimestampMerger]
        MergedQueue[MergedFrameSnapshotQueueSO]
        MetadataTimeline[MetadataTimelineJournalSO]
    end

    subgraph EncodeLayer[40_SingleEncodeProduction]
        VideoInput[VideoFrameInputResolver]
        Compositor[PassthroughCameraLayerCompositor]
        LocalMp4[InstantReplayLocalMp4Recorder]
        EncodeBoundary[SingleEncodeStageBoundary]
        EncodeOutput[SingleEncodeOutputQueueSO]
    end

    subgraph ProductLayer[50_ProductAssembly]
        ProductBuilder[SingleEncodeOutputProductBuilder]
        RealtimeAligned[RealtimeAlignedStreamQueueSO]
        Manifest[SessionArtifactManifestSO]
        MetadataSender[MetadataPacketSender]
        OutputQueue[CaptureOutputQueueSO]
    end

    subgraph DistributionLayer[60_Distribution]
        Sender[LiveNetworkStreamSender]
        Transports[UDP Transports]
        PCReceiver[PCReceiver]
    end

    PCA --> CameraWriter
    CameraRig --> VirtualWriter
    Controller --> ControllerWriter
    Headset --> HeadsetWriter
    CameraRig --> Controller
    CameraRig --> Headset

    CameraWriter --> CurrentSOs
    VirtualWriter --> CurrentSOs
    ControllerWriter --> CurrentSOs
    HeadsetWriter --> CurrentSOs

    RecordingState --> Recorders
    CurrentSOs --> Recorders --> QueueSOs
    QueueSOs --> Merger --> MergedQueue
    MergedQueue --> MetadataTimeline

    MergedQueue --> VideoInput
    VideoInput --> Compositor
    VideoInput --> LocalMp4
    Compositor -.internal helper only.-> VideoInput
    MergedQueue --> EncodeBoundary
    MetadataTimeline --> EncodeBoundary
    LocalMp4 --> EncodeBoundary
    EncodeBoundary --> EncodeOutput --> ProductBuilder
    ProductBuilder --> RealtimeAligned
    ProductBuilder --> Manifest

    MergedQueue --> MetadataSender --> Sender
    OutputQueue --> Sender
    Sender --> Transports --> PCReceiver
```
