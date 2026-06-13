# DataCapture Synchronization

本目录只保留新的 SO 数据总线和时间戳同步层。

完整规划见：

- `Camera_Network_Encoding_SO_Architecture.md`

## 当前数据流

```text
Current SO -> Typed Queue SO -> TimestampMerger -> MergedFrameSnapshotQueueSO
```

Camera frame 是主时钟。其他数据源按 camera timestamp 做 nearest / tolerance 匹配。

`Current SO -> Queue SO` 的入队逻辑由一个通用组件负责：

```text
Assets/SObasic/Runtime/CurrentQueueBridge/CurrentToQueueRecorder.cs
```

Current SO 实现 `ICurrentRecordSource`，Queue SO 实现 `IRecordQueueSink`。因此新增数据路时只需要实现 SO 的 record 转换，不需要再写一份专用 history recorder。

## 目录职责

```text
Synchronization/
├── Runtime/
│   ├── Core/
│   │   ├── Interfaces.cs
│   │   ├── RingBuffer.cs
│   │   ├── SynchronizationClock.cs
│   │   └── TimeStampService.cs
│   ├── DataTypes/
│   │   ├── CameraFrameRecord.cs
│   │   ├── ControllerPoseRecord.cs
│   │   ├── HeadsetPoseRecord.cs
│   │   ├── NetworkDeviceRecord.cs
│   │   ├── VirtualLayerFrameRecord.cs
│   │   └── MergedFrameSnapshotRecord.cs
│   └── Sync/
│       └── TimestampMerger.cs
```

SO 类脚本统一放在：

```text
Assets/SObasic/Runtime/ScriptableObjects/DataCapture/30_TimeSynchronization/
├── Configuration/
├── Current/
└── Queues/
```

## 关键约束

- `Current*SO` 只保存最新状态。
- `CameraFrameQueueSO` 默认只保存 metadata，不保存历史 texture、raw pixels、PNG/JPG bytes。
- `TimestampMerger` 只读 Queue SO，不直接访问 PCA、手柄、头显或外设源。
- 编码和网络发送属于后续层，不放进 Synchronization。

## 旧实现位置

旧 `DataPoint`、`ControllerDataSource`、`SyncManager`、`SyncedDataSnapshot` 原型已删除。当前实现只使用 `Assets/DataCapture/Runtime/30_TimeSynchronization/` 下的正式队列与合帧链路。
