# 架构

本文描述 `com.infinity.virtual-texture` 的当前运行时结构。修改约束见 [AGENTS.md](AGENTS.md)。参数、采样、压缩和测量记录分别见 `docs/` 下各文。

## 1. 范围

本库在 Unity 6 的 URP RenderGraph 路径上运行。公开入口是：

| 类型 | 职责 |
|---|---|
| `VirtualTextureAsset` | 页网格、过滤、层、压缩、物理池和请求队列的参数 |
| `VirtualTextureFeature` | 把 `VirtualTexturePass` 加入 URP Renderer，事件为 `BeforeRenderingPrePasses` |
| `TerrainVirtualTexture` | 定义世界体积，收集地形，创建唯一的运行时 |

`VirtualTextureSystem`、`RequestPipeline`、`IPageProducer`、`PageId` 以及页目录、物理池、间接表均为 `internal`。扩展生产者在运行时程序集内实现 `IPageProducer`，并自行完成虚拟 uv 与自身空间的换算。

程序集固定为四份：

| 程序集 | 位置 |
|---|---|
| `Infinity.Rendering.VirtualTexture.Runtime` | `Runtime/`，允许 unsafe |
| `Infinity.Rendering.VirtualTexture.Editor` | `Editor/` |
| `Infinity.Rendering.VirtualTexture.Shader` | `Shaders/` |
| `Infinity.Rendering.VirtualTexture.Tests` | `Tests/Editor/`，受 `UNITY_INCLUDE_TESTS` 约束 |

目录按职责分开。`Runtime/Core`、`Shaders/Core`、`Shaders/Compress` 只认识虚拟 uv、`PageId`、物理槽和层。地形代码在 `Runtime/Terrain`、`Shaders/Terrain`、`Editor/Terrain`。

## 2. 术语

mip 数值越小越细。第 0 级每边有 `pageCount` 页。根页是 `PageId(0, 0, mipCount - 1)`，`mipCount = tzcnt(pageCount) + 1`。

| 词 | 含义 |
|---|---|
| 页 | 虚拟网格上的一个地址，打包为 `PageId` |
| 物理槽 | 物理图上的一块正方形区域，含页内容与 border |
| 间接表 | 带完整 mip 的 RGBA8 纹理。第 k 级只描述虚拟 mip k |
| 反馈 | 缩小后的 `R32_UInt` 图，每个像素一个 `PageId` |
| 生产者 | 把一页的各层画进 scratch 的 `IPageProducer` |
| 轮次 | 一次回读触发的解码、去重、解析和间接表写入 |

`PageId` 的位布局与着色器 `VTPackPageId` 相同：`[31]` 有效，`[30:26]` mip，`[25:13]` y，`[12:0]` x。`packed == 0` 表示没有请求。坐标各 13 位，因此 `pageCount` 不超过 `1 << 13`。父页是 `(x >> 1, y >> 1, mip + 1)`。

## 3. 数据通路

每一项运行时行为落在下面一条通路上。

| 通路 | 职责 | 位置 |
|---|---|---|
| 反馈 | 按当前 Game 相机、渲染分辨率向上除以 16，绘制 `PageId` 并异步回读 | `VirtualTexturePass`，`FeedbackLayout`，`FeedbackReadback` |
| 页请求 | 回读到达后并行解码、去重计权、解析 | `RequestPipeline`，`RequestJobs` |
| 驻留 | 每级页目录、槽的 owner、槽的新旧顺序 | `PageDirectory`，`PhysicalPool`，`LruOrder` |
| 间接表 | 由驻留槽生成各级矩形写入，着色时整数格子 Load | `IndirectMap`，`Shaders/Core/IndirectMap.shader` |
| 物理页 | 每个启用层一张图，mip 级数为 1 | `PhysicalLayers` |
| 烘焙 | 生产者把 `uvRect`（含 border）画进 scratch | `IPageProducer` |
| 压缩 | 把 scratch 编成块并复制进槽 | `PageEncoder`，`AstcLayout`，`Shaders/Compress` |
| 着色 | lod、间接查找、物理 uv、过滤、层解码、光照 | `VirtualTextureCommon.hlsl`，`VirtualTextureLighting.hlsl` |

地址公式在 `VirtualTextureMath` 与 `VirtualTextureCommon.hlsl` 各有一份：`FilterLod`、`RequestMip`、`PhysicalTexel`、`Footprint`、`IndirectValue`、`PageId` 位布局。改一边时同时改另一边。

## 4. 帧序

`TerrainVirtualTexture.OnEnable` 在 `CanCreate` 通过、尚无 `VirtualTextureSystem.active`、且至少一块地形完整落在体积内时，创建 `TerrainPageProducer` 和 `VirtualTextureSystem`。构造函数订阅 `beginContextRendering` / `endContextRendering`，绑定全局参数，并用根页调用 `RequestPipeline.Schedule`。这次启动不绘制反馈。

`VirtualTextureFeature` 对每个相机入队 `VirtualTexturePass`。`RecordRenderGraph` 在非 Play、没有 active 系统、或相机类型不是 Game 时直接返回。通过这些条件后，单个 Game 相机的录制顺序是：

1. `TryTakeRound`：本帧尚未消费、且请求 Job 已完成时，取走烘焙单和间接表写入。未完成则返回，不阻塞。
2. 若本轮有烘焙单，录制 `VT Bake`。工作集内先逐页把生产者画进 scratch，再编码并复制到物理槽。
3. 录制 `VT Indirect`。`indirectReady` 仍为 false，或本轮 `residencyChanged` 为 true 时，执行 `IndirectMap.Update`。pass 内写入本相机的过滤参数和时间 mip 开关，最后调用 `Publish`。
4. `Publish` 把 `indirectReady` 置 true，并把 `rootReady` 置为根页是否已在 Bake 中出现。
5. 录制时 `rootReady` 已为 true，才创建反馈图并可能提交回读。

Bake 排在 Indirect 之前。根页烘焙发生在 pass 执行阶段，而反馈是否录制取决于录制时的 `rootReady`，因此根页完成的那一帧不画反馈。`beginCameraRendering` 同样读取当时的 `rootReady`，材质切换从后续的 Game 相机开始。

回读完成的回调在主线程调用 `RequestPipeline.Schedule`。Job 链是 `FeedbackDecodeJob`（`IJobParallelFor`，batch 256）→ `RequestCollectJob` → `RequestResolveJob` → `IndirectBuildJob`。`RecordRenderGraph` 只消费已经完成的轮次。同一时间最多一轮请求在途；`Schedule` 在 `m_Scheduled` 时直接返回。同一帧的 `TryTakeRound` 最多成功一次。

## 5. 页目录、队列与物理池

`PageDirectory` 按 mip 分段保存 `PageEntry`。状态只有 `Free`、`Queued`、`Resident`。页到槽的映射在目录的 `slot` 字段。槽到页的映射是 `PhysicalPool` 的 `owner`。槽的新旧顺序只有 `LruOrder`，里面只存槽号。

`RequestResolveJob` 是页状态和 LRU 的唯一写入者。一轮解析的顺序：

1. 每个请求沿父页累加权重，直到根。根页再保证至少有权重 1，空反馈也会触碰它。
2. 权重表里已经 `Resident` 的页执行 `Touch`。`Free` 的页进入候选。
3. 队列里本轮又出现的页更新 `lastRequestRound`。超过 `queueTimeout` 轮未被请求的页回到 `Free` 并离开队列。
4. 空闲候选按权重从高到低排序，同权重时粗页在前，再按 `packed` 排序。队列长度不超过 `queueCapacity`。
5. 从本轮仍被请求的排队页里继续按同一优先级分配。子页的父页尚未 `Resident` 时留在队列。分配个数不超过 `min(bakesPerRound, 容量 - 本轮触碰数)`。

`Touch` 把槽移到刚用过的一端。`Allocate` 调用 `TakeOldest`。因为分配个数不超过「容量减去本轮触碰数」，本轮触碰过的槽不会被选中。根页一旦驻留，就会在每一轮被触碰，因此不会在该轮被淘汰。命中只调用 `Touch`。`residencyChanged` 只在发生分配时变为 true，间接表也只在此时重建。

被淘汰页的目录项回到 `Free`，槽的 owner 改为新页。新页进入本轮烘焙单。

## 6. 间接表

间接表是 `pageCount × pageCount`、mip 级数等于 `mipCount` 的 `R8G8B8A8_UNorm` 纹理，`autoGenerateMips` 关闭，过滤为 Point，包裹为 Clamp。第 `level` 级对应虚拟 mip `level`。

`IndirectBuildJob` 在 `residencyChanged` 为 false 时不产生写入。为 true 时收集全部驻留槽，按粗页在前排序。对每一级，写入所有 `page.mip >= level` 的页；`Footprint` 给出该级上的矩形。细页后画，覆盖祖先留下的格子。每个格子最终是「这一格自己，或最近的驻留祖先」。

纹素按 RGBA8 打包：R、G 是槽坐标，B 是页的实际 mip，A 为 255 表示有效。写入着色器 `Hidden/VirtualTexture/IndirectMap` 把 uint 拆成 0–1 后画进目标级。着色器用 `LOAD_TEXTURE2D_LOD` 按整数格子和显式 mip 读取，再乘 255 还原。A 大于 127.5 视为有效。

HLSL 的 `int` / `uint` 全局量使用 Integer API：`_VTLayerCount`、`_VTNormalIsRG`、`_VTTemporalEnabled`、`_VTTemporalSeed`，以及间接写入用的 `_IndirectWriteBase`。间接表资源本身是 UNorm 纹理，承载的是上述打包值。

`Publish` 是 CPU 侧标志，发生在 Indirect pass 执行到末尾时。它表示烘焙与间接表命令已经按 pass 顺序录制并执行到这一步，供下一帧的材质切换和反馈录制读取。

## 7. 物理图与烘焙

每个启用层一张物理图，边长 `poolSize × slotTexels`，`slotTexels = pageTexels + 2 × pageBorder`。槽按行主序铺在这张正方形图上。物理图的 mip 级数为 1，过滤为 Bilinear，`anisoLevel` 为 0。border 只覆盖物理图上的一次双线性过滤。

未压缩层是 RenderTexture。压缩层是无 mip 的 Texture2D，由块图 `CopyTexture` 写入。scratch 始终是未压缩的 RGBA8：颜色层 sRGB，法线与 Mask 线性，边长等于槽边长。一个工作集里每层有 `batchSize` 张 scratch。

`batchSize` 取格式上限和 `bakesPerRound` 的较小值。BC、ETC2、ASTC 4×4 的格式上限是 4；None、ASTC 6×6、ASTC 8×8 的格式上限是 1。一个工作集先逐页 MRT 烘焙，再编码该集里的启用层，最后复制到槽。格式上限大于 1 时，块图按页和层分开保存，编码全部完成后再复制。格式上限为 1 时，一层编码并复制后才复用同一张块图。

scratch、块图和物理图在烘焙帧显式导入 RenderGraph。Bake pass 对它们具有读写访问。格式细节、通道约定和 `CanCreate` 的平台条件见 [docs/compression.md](docs/compression.md)。

## 8. 采样与反馈

着色导数在虚拟像素空间计算：`uv × pageCount × pageTexels`。`EPageFilter.Bilinear` 做一次间接查找和一次物理 0 级采样。`Trilinear` 在 `floor(lod)` 与 `ceil(lod)` 各查一次。两级落到同一驻留页时只采样一次；落到不同驻留页时采样两次，并在 texel 空间按 lod 的小数部分插值。`Anisotropic` 用 `anisoSamples` 限制沿长轴的查找次数，每个查找点走同一次 lod 上的三线性。

`temporalMip` 默认关闭。打开时只影响三线性（以及经由三线性的各向异性）：用片元哈希在相邻两级中选一级，并把另一次查找收成同一级。反馈不读这个开关，始终按确定性规则请求一个页；祖先在 CPU 上补齐。各向异性的多次查找共用这一次 mip 选择。

反馈图尺寸为 `max(1, ceil(renderSize / 16))`，格式 `R32_UInt`，深度 `D16_UNorm`。绘制使用去掉抖动的投影，导数再除以实际缩小倍数。同一时刻最多一个异步回读。多个 Game 相机轮流获得回读名额，且请求通路忙或回读在途时不再提交。

启用条件、解码和全局参数见 [docs/sampling.md](docs/sampling.md)。

## 9. 地形生产者

`TerrainVirtualTexture` 在启用时用 Transform 位置计算 XZ 体积。边长来自 `EVirtualTextureVolumeSize`，中心按半个体积取整，避免平移组件时虚拟 uv 出现亚页偏移。`_VTVolumeRect` 的 xy 是原点，zw 是宽高。

`Terrain.activeTerrains` 里，轴对齐包围矩形完全位于体积内的地形进入生产者，并在根页就绪后换上 `Landscape/TerrainLit_VT`。只与体积相交、并未完全包含的地形保持原材质，并输出警告。启用之后新出现或移入的地形不会自动重新收集。

材质切换挂在 `beginCameraRendering`。当前相机是 Game、系统就是 active 系统、且 `rootReady` 为 true 时，这些地形的 `materialTemplate` 换成虚拟纹理材质，`basemapDistance` 设为 `float.MaxValue`。条件不满足时恢复启用时备份的材质和 basemap 距离。因此 Scene 相机开始渲染时，这些地形会暂时回到原材质；下一次合格的 Game 相机再换回。跨出体积的地形不在这份列表里。

虚拟纹理材质的前向、深度、阴影、拾取和反馈 pass 不声明地形 splat。splat、图层贴图和 stochastic 采样只出现在 `Hidden/VirtualTexture/TerrainBake`。颜色、法线和 Mask 共用同一组偏移与权重，`SampleGrad` 使用偏移前的连续 uv 梯度。

禁用组件时释放系统与生产者，并恢复材质。`VirtualTextureFeature.Dispose` 只丢掉 pass 对象，不拥有系统。

## 10. 不变式

后续修改保持下面这些关系：

- 页状态、页到槽、槽到页、槽的新旧顺序各有一处数据。解析 Job 是它们的唯一写入者。
- 渲染录制不执行解析。命中不改间接表，也不重新烘焙。
- 根页走同一条请求通路进入驻留，并在之后的每一轮解析里被触碰。
- 物理内容的 Bake pass 早于更新间接表的 Indirect pass。反馈和材质切换读取已经发布的 `rootReady`。
- 底层目录不出现地形类型。新生产者只增加地址换算和烘焙绘制。
- 着色器全局纹理与参数使用 `_VT` 前缀，名称与内容一致。int / uint 全局量走 Integer API。
- C# 与 HLSL 的地址公式保持同式。

## 11. 实现边界

当前实现同时只允许一个系统。VT pass 只在 Play 模式下的 Game 相机录制。物理图提供 0 级双线性采样；三档过滤和各向异性次数都由间接表查找完成。

`CanCreate` 检查参数、纹理尺寸上限、`R32_UInt` 反馈与异步回读、各层采样和 scratch 格式，以及压缩时的 compute 与 `CopyTexture`（`DifferentTypes` 和 `RTToTexture`）。任一失败就返回错误，调用方不创建系统，也不改用另一种压缩格式。

反馈缓冲和请求轮次都是单在途。时间 mip 的相机条件见采样文档；不满足时过滤保持确定性。间接表写入着色器按 `UNITY_UV_STARTS_AT_TOP` 处理 y 方向；2026-10-22 的画面测量只在 Metal 上完成，见 [docs/validation.md](docs/validation.md)。
