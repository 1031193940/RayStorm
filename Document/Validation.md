# 修复验证记录

目标工程版本：Unity 2022.3.27f1，内置渲染管线。

本机原 2022.3 安装不完整，原工程没有升级。集成验证使用隔离副本和 Unity 6000.6.0f1；该结果不能替代一次 2022.3 编辑器验收。

## 验证项目

- 八叉树：分割面三角形、远坐标、轴平行边界、512 条射线与暴力遍历最近命中对照。
- 场景：支持的 Renderer、缺少法线/切线/材质、临时网格所有权与相机 Layer。
- 调度：奇数尺寸、尾块裁剪、完整像素覆盖、五轮 816 samples/pixel 和帧数限制。
- GPU：浮点 HDR/计数、不同 spp 加权、越界保护、进度叠加清除、环境和自发光、双面漫反射。
- 渲染器：透视/正交射线、多实例环境隔离、尺寸变化重启、子网格顶点复用与清理。
- Player：Windows Development Build、完成一帧、检查输出像素、写入 persistentDataPath。

## 结果

2026-11-10 实际执行结果：

| 检查 | 环境 | 结果 |
| --- | --- | --- |
| 三个 Compute Shader 独立编译 | DXC，Shader Model 6.0 | 通过 |
| 全部 C# 与 Editor 脚本编译 | Unity 6000.6.0f1 | 通过 |
| SceneValidation | Unity 实际 Vector3/Mathf 与原生场景对象 | 通过 |
| RayStormRuntimeValidation | Unity Editor | 通过 |
| RayStormGpuValidation | NVIDIA GeForce RTX 5080 / Direct3D 11 | 通过 |
| RayStormRendererValidation | 同上，真实 GPU 读回和原生资源清理 | 通过 |
| Windows Development Player 构建 | 隔离测试场景，显式 Shader 引用 | 通过 |
| Player 冒烟测试 | 128×96，一帧完成，有限像素值、采样数和 JPEG 文件检查 | 通过 |

统一验证返回 `passed: true`；Player 记录 `RAYSTORM_PLAYER_SMOKE_PASSED`，输出到其独立的 persistentDataPath。未改动原工程的 Unity 版本或使用 Unity 6 回写原工程资产。

Unity 6 对为兼容 2022.3 保留的 `FindObjectsOfType` 给出弃用警告；构建编辑器退出时出现 Unity 内部 JobTempAlloc 提示，未据此宣称整个 Unity 进程没有内存问题。回归验证已确认本渲染器持有的 RT、Cubemap 和 ComputeBuffer 正确释放。

## 尚未覆盖的环境

Unity 2022.3 原版本编辑器、Metal/Vulkan/移动端、长时间运行及大规模场景性能仍需在对应环境验收。本次没有把独立 DXC 编译或 Unity 6 验证等同于这些环境已通过。
