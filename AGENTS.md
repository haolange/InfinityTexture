# AGENTS.md

修改本库时遵守本文件。运行时结构以 [ARCHITECTURE.md](ARCHITECTURE.md) 和当前源码为准；二者不一致时先核对源码，再同时改正文档。参数、采样、压缩和测量记录在 `docs/`。

本库不新增 `DESIGN.md`。该文件名不用于架构、界面或视觉规范。

## 平台

- 只支持 Unity 6（6000.0）+ URP RenderGraph。
- 渲染只有 `ScriptableRenderPass.RecordRenderGraph` 这一条路径。不写 `Execute`，不写 Compatibility Mode 分支。

## 数据通路

每一项行为落在 [ARCHITECTURE.md](ARCHITECTURE.md) 第 3 节的一条通路上。不另写并行实现，不用临时分支绕开中间状态。

| 通路 | 位置 |
|---|---|
| 反馈 | `VirtualTexturePass`，`FeedbackLayout`，`FeedbackReadback` |
| 页请求 | `RequestPipeline`，`RequestJobs` |
| 驻留 | `PageDirectory`，`PhysicalPool`，`LruOrder` |
| 间接表 | `IndirectMap`，`Shaders/Core/IndirectMap.shader` |
| 物理页 | `PhysicalLayers` |
| 烘焙 | `IPageProducer` |
| 压缩 | `PageEncoder`，`AstcLayout`，`Shaders/Compress` |
| 着色采样 | `Shaders/Core/VirtualTextureCommon.hlsl`，`VirtualTextureLighting.hlsl` |

## 底层与生产者

- `Runtime/Core`、`Shaders/Core`、`Shaders/Compress` 不出现 `Terrain`。底层只认识虚拟 uv、`PageId`、物理槽和层。
- 生产者只做两件事：虚拟 uv 与自身空间的换算；按 `PageBakeRequest` 把启用层画进已经绑定的 scratch。
- 新生产者只新增生产者类型、地址换算和烘焙着色器。间接表、物理池、反馈解析、LRU 和压缩留在现有通路，不复制一份。
- `VirtualTextureMath` 与 `VirtualTextureCommon.hlsl` 的 `FilterLod`、`RequestMip`、`PhysicalTexel`、`Footprint`、`IndirectValue` 和 `PageId` 位布局保持同式，改动时两边一起改。

## 状态与并发

- 页到槽只写在 `PageDirectory`。槽到页只写在 `PhysicalPool.owner`。槽的新旧顺序只写在 `LruOrder`，其中只有槽号。
- `RequestResolveJob` 是页状态和 LRU 的唯一写入者。
- 已驻留页被请求时只 `Touch`。`residencyChanged` 只在 `Allocate` 时变为 true。命中不改间接表，不重新烘焙。
- 分配个数不超过 `min(bakesPerRound, 容量 - 本轮触碰数)`，本轮触碰过的槽不进入 `TakeOldest`。
- 根页由构造时的同一次 `Schedule` 进入请求通路。每一轮解析给根页至少权重 1。根页驻留后每轮都被触碰。不另加根页锁或第二套驻留状态。
- 子页在父页 `Resident` 之后才分配。
- `RecordRenderGraph` 只调用 `TryTakeRound` 消费已完成结果。录制 CommandBuffer 时不跑解析。
- 请求通路同一时间一轮。反馈回读同一时间一个在途请求。渲染 pass 在物理 Bake 之后更新间接表，再 `Publish`。反馈录制和 Game 相机材质切换读取已经发布的 `rootReady`。

## 采样、反馈与压缩

- 间接表是 `R8G8B8A8_UNorm`，第 k 级只描述虚拟 mip k。读取使用整数格子和显式 mip 的 `LOAD_TEXTURE2D_LOD`。打包值是槽坐标、页 mip 和有效标记。
- 绑定 HLSL `int` / `uint` 时使用 Integer API（`SetGlobalInteger`、`SetInteger`）。不要把这些量写成 float 再截断。
- 物理图 mip 级数为 1，`anisoLevel` 为 0。双线性、三线性和各向异性都通过间接表查找实现。各向异性 taps 共用一次 mip 选择。
- `temporalMip` 默认关闭。仅当资产开启、过滤不是 Bilinear、根页已发布，且 `TemporalMipAvailable` 确认当前是带有效上一帧 TAA 积累和像素抖动的 Game / `UniversalRenderer` 基础相机时，才把 `_VTTemporalEnabled` 置 1。其他状态保持确定性过滤。反馈不读该开关，请求确定性 mip；祖先由解析补上。
- 压缩按层选择：BC 的颜色 / 法线 / Mask 为 BC1 / BC5 / BC3；ETC2 的颜色为 RGB，数据为 RGBA。块图按 64 位或 128 位分配。ASTC 块图为 128 位。不用一份固定 128 位预算描述所有层。
- 格式决定的工作集上限：BC、ETC2、ASTC 4×4 为 4 页；None、ASTC 6×6、ASTC 8×8 为 1 页。实际上限还要再与 `bakesPerRound` 取较小值。顺序是逐页 MRT 烘焙、编码该工作集的启用层、复制到槽。scratch 和块图显式导入 RenderGraph。块图只在不会覆盖尚未复制的结果时复用。
- 生产者法线 scratch 为 G = y、A = x。BC5 编码为 R = x、G = y。采样端仅在 BC 时用 `_VTNormalIsRG` 还原到同一解码。颜色的 Linear / sRGB 转换留在烘焙目标格式、编码和硬件采样这三处。
- 地形烘焙用连续图层 uv 生成 stochastic 偏移。颜色、法线、Mask 共用位置和权重。`SampleGrad` 使用偏移前的 uv 梯度。
- 地形材质切换只发生在 `TerrainVirtualTexture` 已经收集、且完整位于体积内的地形上。VT 材质的前向、深度、阴影和反馈不声明 splat，不使用 additive 或原生 basemap pass。源图层只在烘焙着色器里采样。禁用后恢复原材质和 basemap 距离。

## 命名

- 类型、函数、字段都不使用 F 前缀，也不引入新的类型前缀。
- 枚举保留 E 前缀。
- 与 Unity 自带类型重名时，换一个库内不带前缀的名字。
- 着色器全局名使用 `_VT` 前缀，并且名副其实。

## 不留兼容层

- 改名、删除时，仓库里的代码、shader 名、`.asset` / `.mat` / `.prefab` YAML 一次改完。
- 不留 `[Obsolete]`、旧名转发、兼容字段或旧 shader 名。
- 旧字段、未接入的演示、走不到的路径，直接删除。

## 代码约定

- asmdef 保持 `Runtime`、`Editor`、`Shaders` 和 `Tests/Editor` 的划分。
- 热路径保持 Burst、`UnsafeUtility.Malloc` / `NativeArray` 和 `ref` 访问。
- 注释只写做什么、为什么。
- 每个新文件和新目录都提交手写 `.meta`。GUID 为 32 位十六进制，在本包内唯一。以 git 包安装时目录只读，Unity 不会补生成 `.meta`。
- 移动文件时连同 `.meta` 一起移动，GUID 不变。

## 测试

EditMode 测试在 Unity 6 Test Runner 中运行，程序集 `Infinity.Rendering.VirtualTexture.Tests`。测试使用内存中的模拟数据，覆盖：

- 页状态、唯一请求、祖先链、LRU
- 间接表 mip 的查找数学，以及 GPU 上的级写入
- 过滤 lod、请求 mip、时间 mip 的启用条件
- 压缩块尺寸、层格式、ASTC 表和 GPU 复制
- 地形烘焙的页边界与图层对齐
- 反馈 `PageId` 的位布局和回读保位

不把反馈图、间接表、物理页或压缩结果写成文件。临时目录和临时文件用完即删。

## 验证清单

```sh
# 以下命令都不应有输出
# F 前缀的类型、函数、字段
grep -rnE '\b(class|struct|enum|interface)\s+F[A-Z]' Runtime Editor Shaders Tests
grep -rnE '\b[A-Za-z0-9_<>\[\]]+\s+F[A-Z][A-Za-z0-9_]*\s*(\(|;|=)' --include=*.cs --include=*.hlsl --include=*.compute --include=*.shader Runtime Editor Shaders Tests
# 底层不出现 Terrain
grep -rn 'Terrain' Runtime/Core Shaders/Core Shaders/Compress
# 已删除的状态与旧全局名
grep -rnE 'isReady|notLoading|isNull|activeFrame|activePageMap|_Physcis|_PageTableTexture' Runtime Shaders
```
