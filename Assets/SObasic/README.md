# SObasic - Unity ScriptableObject 架构

> 从 Meta Unity-FirstHand 项目提取的完整 SO 架构实现  
> **13/14 核心模式** | **21个组件** | **0 编译错误**

## 🎯 核心特性

- ✅ **接口驱动设计** - IActiveState、IProperty、ISelector
- ✅ **防抖动机制** - ConfigurableActiveState 的 `_minActiveTime`
- ✅ **观察者模式** - ActiveStateObserver 抽象基类
- ✅ **引用包装** - ReferenceActiveState 序列化接口引用
- ✅ **属性系统** - PropertyBehaviour<T> 泛型基类
- ✅ **范围验证** - FloatRange/FloatRanges
- ✅ **自定义 Inspector** - 完整的 PropertyDrawer
- ✅ **性能标记** - Profiler.BeginSample 集成

## 📦 快速开始

### 1. 基础状态检测

```csharp
using SObasic;
using UnityEngine;

public class MyComponent : MonoBehaviour
{
    [SerializeField] private ReferenceActiveState _targetState;
    
    void Update()
    {
        if (_targetState.Active)
        {
            // 目标激活时的逻辑
        }
    }
}
```

### 2. 观察者模式

```csharp
public class MyObserver : ActiveStateObserver
{
    protected override void HandleActiveStateChanged()
    {
        Debug.Log($"State changed: {Active}");
    }
}
```

## 📚 文档导航

### 新手入门
- [使用指南](docs/00_USAGE_GUIDE.md) - 如何使用这套架构
- [接口模式](docs/01_Interface_Patterns.md) - IActiveState、ISelector 详解
- [SO 模式](docs/02_ScriptableObject_Patterns.md) - SO 数据容器

### 进阶学习
- [**高级模式**](docs/ADVANCED_PATTERNS.md) ⭐ - 14个核心模式详解
  - 防抖动、自动依赖注入、更新时机控制
  - Profiler 标记、Conditional* 组件族
  - PropertyBehaviour 属性系统

## 🏗️ 架构分层

```
┌─────────────────────────────────────────────┐
│  Interface 层                                │
│  - IActiveState, IProperty, ISelector       │
├─────────────────────────────────────────────┤
│  Reference 包装层                            │
│  - ReferenceActiveState (序列化包装)        │
├─────────────────────────────────────────────┤
│  MonoBehaviour 实现层                        │
│  - ConfigurableActiveState (防抖动)         │
│  - ActiveStateObserver (观察者基类)          │
│  - Conditional* 组件族 (条件控制)            │
├─────────────────────────────────────────────┤
│  ScriptableObject 数据层                     │
│  - IntValueAsset, SurfaceTag                │
└─────────────────────────────────────────────┘
```

## 🔥 核心组件速查

| 组件 | 用途 | 关键特性 |
|------|------|---------|
| **ConfigurableActiveState** | 状态控制 | 防抖动 `_minActiveTime` |
| **ActiveStateObserver** | 观察者基类 | Reset()、When flags、Profiler |
| **ReferenceActiveState** | 接口引用 | 序列化、自定义Inspector |
| **ChildCountActiveState** | 子对象检测 | FloatRanges 范围匹配 |
| **IsInViewActiveState** | 视野检测 | FOV、距离检测 |
| **PropertyBehaviour<T>** | 属性系统 | 泛型基类、自动通知 |

## 📊 完成度

- ✅ **13/14** 核心模式已实现 (92.8%)
- ✅ **21** 个文件
- ✅ **0** 编译错误
- ❌ InteractableActiveState (ISDK专用，不适合通用架构)

---

**源自**: Meta Unity-FirstHand Comprehensive Sample  
**命名空间**: `SObasic`  
**Unity 版本**: 2021.3+
