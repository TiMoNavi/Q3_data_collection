# Quest APK 数据采集链路调试原则

Last updated: 2026-06-13

## 目的

这份文档定义在 Quest 3 上调试数据采集链路时的固定流程。目标不是“看起来启动过”，而是每次都能回答三个问题：

- APK 是否安装到了正确的头显。
- 应用是否真的进入了采集场景，并且没有停在 Horizon Link 或系统界面。
- 视频采集链路是否产出了本地 MP4；如果没有，ADB/logcat 是否能明确指出卡在哪一层。

官方前提：Meta 当前文档说明 ADB 是开发阶段与 Meta Quest 头显通信、安装应用和执行调试命令的主工具；多设备连接时必须指定设备 id。调试时优先用 hzdb 包装的设备、应用、日志、截图和文件命令，必要时再落到原始 `adb`。

## 默认门控选择

默认 Android APK gate 使用 `LocalMp4EndToEndDebugRunner`。

原因：

- 它覆盖本地采集到本地 MP4 的主线：Recording harness、Current video input、MP4 writer、finalize、manifest。
- 它会强制进入 `LocalOnly + LocalFile + LocalMp4Save`，不依赖 PC Receiver。
- 它的日志层名是 `LocalMp4E2E.*`，适合在 ADB/logcat 中直接定位卡点。

`DataCaptureSODebugPipeline` 保持不自动运行。它更适合排查 SO access、Debug JPEG、网络发送、PC Receiver evidence 等联机链路；如果作为默认 APK gate，失败可能只是 PC discovery 或接收端未就绪，不代表本地采集和 MP4 产物链路失败。

开始现场调试前必须确认：

```text
Assets/Scenes/SampleScene.unity
LocalMp4EndToEndDebugRunner.runOnStart = 1
DataCaptureSODebugPipeline.runOnStart = 0
```

如果 `LocalMp4EndToEndDebugRunner.runOnStart` 仍为 `0`，启动 APK 后不会自动跑门控，ADB 只能看到普通运行日志，不能证明链路是否通过。

## 标准流程

### 0. 编译和构建等待规则

遇到 Unity 编译、资源导入、脚本 domain reload、Android 平台切换或 APK 构建时，不要用 30 秒级别的工具超时直接判定失败。先等待：

```powershell
Start-Sleep -Seconds 900
```

也就是 15 分钟。15 分钟后再检查 Unity Console、Editor 状态、APK 时间戳和构建日志。只有在 15 分钟后仍无新日志、无 APK 更新时间、Unity 无响应，才按“构建卡死或失败”处理。

### 1. 安装最新 APK

使用当前要验证的 APK，不混用旧包。当前项目常用自动测试包路径：

```text
Builds/Q3DataCollection_autotest.apk
```

安装前先列设备，记录目标 serial。多设备或 USB/Wi-Fi 同时连接时，所有安装、启动、日志、文件命令都必须指定同一个目标设备。

```powershell
hzdb device list
hzdb app install Builds/Q3DataCollection_autotest.apk
```

如果使用原始 ADB：

```powershell
adb devices
adb -s <device_id> install -r Builds/Q3DataCollection_autotest.apk
```

### 2. 点亮 Quest 屏幕

用设备命令唤醒头显，避免 APK 已启动但头显处于睡眠或近距传感器状态不明确。

```powershell
hzdb device wake
```

必要时确认电量和温度，低电量、过热、系统节流会污染调试结果。

```powershell
hzdb device battery
```

### 3. 记录启动前系统界面状态

启动应用前可以先看一次当前画面，用于记录环境状态。Quick Actions、Dashboard、Home 或其他系统 overlay 出现在启动前不作为硬阻塞；正常情况下，启动 APK 后系统面板会自动收起，应用会重新获得前台焦点。

```powershell
hzdb capture screenshot -o captures/pre_launch_state.png
```

如果启动后系统面板仍遮挡应用、权限弹窗仍未处理，或画面停在 Horizon Link / Home / Dashboard，再判定为前台状态异常。这个检查的重点是“启动后是否真正进入项目应用和 XR session”，不要因为启动前残留的系统面板直接中止调试。

### 4. 启动软件

先确认包名。当前 `ProjectSettings.asset` 里的 Android application id 是：

```text
com.UnityTechnologies.com.unity.template.urpblank
```

如果构建脚本覆盖了包名，以 `hzdb app list` 或 `hzdb app info` 的结果为准。

```powershell
hzdb app list
hzdb app launch <package>
```

使用原始 ADB 时：

```powershell
adb -s <device_id> shell monkey -p <package> 1
```

### 5. 确认软件正确打开

启动后立即截图，并同时看应用生命周期日志。通过条件是：画面进入项目应用，不停留在 Horizon Link、Home、权限弹窗或黑屏。

```powershell
hzdb capture screenshot -o captures/post_launch_state.png
hzdb adb logcat --tag ActivityManager --level I -n 200
```

如果应用黑屏或闪退，先看 crash/system 日志，不进入采集链路判断。

```powershell
hzdb adb logcat --buffer crash --level E -n 300
hzdb adb logcat --regex "FATAL EXCEPTION|native crash|SIGABRT|SIGSEGV|ANR in|OutOfMemoryError" -n 1000
```

### 6. 启动视频采集链路

调试原则上由 `LocalMp4EndToEndDebugRunner.runOnStart = 1` 自动触发采集 gate。启动应用后应在 Unity/logcat 中看到：

```text
[SO-Debug][PASS][LocalMp4E2E.Start]
[SO-Debug][ACTION][LocalMp4E2E.00]
```

如果看不到 `LocalMp4E2E.Start`，先判定为“gate 没启动”，不要继续分析 MP4 是否存在。

推荐日志过滤：

```powershell
hzdb adb logcat --tag Unity --level I --regex "SO-Debug|LocalMp4E2E|SO-Access|FAIL|Exception" --follow
```

如果 hzdb 的 regex/tag 组合不可用，使用原始 ADB 全量拉取后本地过滤：

```powershell
adb -s <device_id> logcat -d | findstr /i "SO-Debug LocalMp4E2E SO-Access FAIL Exception"
```

### 7. 判断是否有产物

优先从 `LocalMp4E2E.04.MP4Finalize` 或 `LocalMp4E2E.05` 日志读取 `manifest.mp4Path`、`manifest.byteLength`、`manifest.frameCount` 和 `finalize.decision`。

通过条件：

```text
[SO-Debug][PASS][LocalMp4E2E.04.MP4Finalize]
[SO-Debug][PASS][LocalMp4E2E.05]
[SO-Debug][PASS][LocalMp4E2E.Pipeline]
manifest.byteLength > 0
manifest.frameCount > 0
```

本地 MP4 默认写在 Android app persistent data 下：

```text
/sdcard/Android/data/<package>/files/LocalMp4/
```

拉取产物：

```powershell
hzdb files ls /sdcard/Android/data/<package>/files/LocalMp4/
hzdb files pull /sdcard/Android/data/<package>/files/LocalMp4/ captures/<session_name>/
```

拉到本地后再分析 MP4、manifest、metadata sidecar。不要只用“文件存在”作为通过条件；必须同时看 byte length、frame count、manifest/finalize 状态。

如果没有产物，先找明确卡点：

```powershell
hzdb adb logcat --tag Unity --level I -n 2000
hzdb adb logcat --regex "SO-Debug.*FAIL|LocalMp4E2E.*FAIL|blocker=|timeout=|HasException|MP4 writer|CurrentVideoFrameInput" -n 2000
```

失败日志需要保留完整字段：

```text
[SO-Debug][FAIL][<layer>] target=<target> condition=<expected> actual=<actual> blocker=<reason> timeout=<seconds> fields=<state>
```

没有 `FAIL` 也没有 `PASS Pipeline` 时，不算通过。优先判断为 gate 未启动、日志过滤过窄、应用未进入前台、或运行时间不足。

### 8. 退出软件并息屏

调试结束必须停止应用，避免上一次 session 状态污染下一轮。

```powershell
hzdb app stop <package>
```

确认退出后再息屏或让设备自然休眠。下一轮开始前重新执行“点亮屏幕”和“确认不在 Horizon Link 界面”。

## 本次现场新增规则：黑屏、息屏和半成品 MP4

2026-06-13 的现场调试暴露出一个重要问题：应用链路可能已经启动并写出了 `.mp4` 字节，但头显在录制或 finalize 窗口内进入 `SCREEN_OFF` / `STANDBY`，Unity Activity 收到 `OnApplicationPause(true)`、`APP_CMD_STOP` 或 OpenXR session stop。此时 MP4 writer 会被系统生命周期打断，设备目录里可能留下一个有大小的 `.mp4`，但它不是可发布产物。

### 运行前必须锁定唤醒状态

每轮启动 APK 前都必须检查 proximity 和电源状态：

```powershell
hzdb device proximity_get
hzdb device battery
```

可接受状态：

```text
prox_override = CLOSE
power_state = HEADSET_MOUNTED
```

如果看到以下任一状态，先不要启动 APK：

```text
enabled = true
prox_override = DISABLED
power_state = STANDBY
```

恢复命令：

```powershell
hzdb device wake
hzdb device proximity_set --enable false
adb -s <device_id> shell svc power stayon true
adb -s <device_id> shell settings put global stay_on_while_plugged_in 7
```

恢复后再次执行 `hzdb device proximity_get`，确认回到 `CLOSE / HEADSET_MOUNTED`。如果无法恢复，不跑 gate；结果记为 `INCONCLUSIVE`。

### 录制窗口内不要主动截图

截图用于确认启动前和启动后的可见状态，但不要在 `LocalMp4E2E` 的录制和 finalize 窗口内调用 `metacam` 或系统截图。现场出现过截图失败、Shell/显示状态异常和应用被 pause 的组合；为了避免污染门控，推荐流程是：

```text
启动前截图 -> 清 logcat -> 启动 APK -> 等 gate 结束 -> 查日志和文件 -> 必要时再截图
```

如果必须截图，先判断这轮结果为“环境观察轮”，不要把它作为严格门控结论。

### 看到 SCREEN_OFF 或 APP_CMD_STOP 时的判定

logcat 中出现以下信号时，本轮不直接归因到采集链路失败：

```text
SCREEN_OFF
OnApplicationPause(true)
APP_CMD_STOP
XR_SESSION_STATE_STOPPING
ActivityLifecycleListener: onActivityStopped
Display device changed state: "Built-in Screen", OFF
```

如果这些信号发生在 `LocalMp4E2E.04.MP4Writer` 之后、`LocalMp4E2E.04.MP4Finalize` 或 `LocalMp4E2E.05` 之前，优先判定为“系统息屏/生命周期打断导致 finalize 未完成”。处理措施是恢复 `proximity_set --enable false`、确认 `HEADSET_MOUNTED`，重新跑一轮。

### 有 MP4 文件不等于通过

设备目录中出现 `.mp4` 只能说明 writer 曾经开始写入。通过条件仍然必须满足：

```text
[SO-Debug][PASS][LocalMp4E2E.04.MP4Finalize]
[SO-Debug][PASS][LocalMp4E2E.05]
manifest.byteLength > 0
manifest.frameCount > 0
```

拉回本地后还要用 `ffprobe` 或等价工具检查：

```powershell
ffprobe -hide_banner -v error -show_format -show_streams <file.mp4>
```

如果 `ffprobe` 报：

```text
moov atom not found
Invalid data found when processing input
```

说明 MP4 尚未 finalize，是半成品。不要按 PASS 处理；结合 logcat 查是否存在 `SCREEN_OFF`、`APP_CMD_STOP` 或 writer/finalize failure。

### CurrentVideoFrameInput 参数等待不是致命失败

现场曾出现 MP4 writer 在 `CurrentVideoFrameInputSO` 的分辨率、帧率或码率尚未写入时过早启动，导致 `Blocked: CurrentVideoFrameInputSO has no valid encoding parameters.`。修复后的预期行为是先记录：

```text
Waiting: CurrentVideoFrameInputSO has no valid encoding parameters.
```

然后继续重试，直到参数有效并出现：

```text
[SO-Debug][PASS][LocalMp4E2E.04.Input]
[SO-Debug][PASS][LocalMp4E2E.04.MP4Writer]
```

因此 `Waiting:` 只表示启动时序等待，不算失败；只有超时后出现 `[SO-Debug][FAIL][LocalMp4E2E.04.MP4Writer]` 或 `mp4.hasFailure=True` 才算该层失败。

## 结果判定

### PASS

满足全部条件才算通过：

- APK 安装到正确 Quest。
- 启动后截图确认应用在前台。
- logcat 出现 `LocalMp4E2E.Start`。
- logcat 出现 `LocalMp4E2E.Pipeline` PASS。
- 设备上存在 MP4/manifest/metadata sidecar，且 manifest 显示 frame count 和 byte length 为正。
- 拉到本地后 MP4 可分析。

### FAIL

以下任一情况都算失败：

- `LocalMp4E2E.Start` 没出现。
- 出现 `[SO-Debug][FAIL][LocalMp4E2E.*]`。
- 应用 crash、ANR、黑屏或被系统界面抢焦点。
- MP4 文件不存在、byte length 为 0、frame count 为 0。
- MP4 文件存在但 `ffprobe` 报 `moov atom not found`，或没有 finalize/manifest PASS。
- manifest/finalize 状态不是可发布完成态。

### INCONCLUSIVE

以下情况不直接归因到采集链路：

- 目标设备不明确，或命令打到了错误 serial。
- 启动后 Horizon Link、Home、权限弹窗、系统 overlay 仍然抢焦点，导致应用未进入前台。
- proximity 未锁定为 `CLOSE`，设备进入 `STANDBY`，或日志出现 `SCREEN_OFF` / `APP_CMD_STOP`。
- 在录制/finalize 窗口内调用截图工具并导致 Shell/显示状态异常。
- 运行包不是刚安装的测试 APK。
- gate 未自动启动。
- logcat 在复现前没有开始采集，关键日志可能已被挤出 buffer。

## 现场记录模板

每次调试记录至少保存：

```text
date:
apk:
package:
device_serial:
pre_launch_screenshot:
post_launch_screenshot:
logcat_file:
result: PASS / FAIL / INCONCLUSIVE
first_fail_layer:
blocker:
artifact_remote_dir:
artifact_local_dir:
mp4_path:
manifest_path:
frame_count:
byte_length:
notes:
```

## 调试纪律

- 先确认 gate 入口，再分析链路。`runOnStart = 0` 时没有自动门控结果。
- 先截图确认前台状态，再判断日志和产物。
- 先看结构化 `SO-Debug` 结果，再猜代码原因。
- 有产物就拉本地分析；没产物就定位第一条 `FAIL` 或第一处缺失的 `PASS`。
- 每轮结束都 stop app，下一轮从干净安装/启动/日志开始。
