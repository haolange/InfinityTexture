# AGENTS.md

本文件是本仓库唯一的设计与协作约定，不新增 DESIGN.md。

## 平台

- 只支持 Unity 6（6000.0）+ URP RenderGraph。
- 渲染只有一条 RenderGraph 路径：`ScriptableRenderPass.RecordRenderGraph`。不写 `Execute` / Compatibility Mode 分支。

## 数据通路

每一项行为都必须落在下面某一条通路上。不允许另写并行实现，也不允许用临时分支绕开中间状态。

| 通路 | 职责 | 位置 |
|---|---|---|
| 反馈 | 用当前相机投影、按渲染分辨率 / 16 绘制 PageId，异步回读 | `VirtualTextureFeature` Feedback / Readback Pass，`FeedbackReadback` |
| 页请求 | 回读到达后：并行解码 → 去重并统计权重 → 解析（补祖先、触碰、排队、分配） | `RequestJobs.cs` |
| 驻留 | 每级页目录（状态：空闲 / 排队 / 已驻留）与物理槽 LRU | `PageDirectory`、`PhysicalPool` |
| 间接表 | 带完整 mip 的 GPU 间接表；第 k 级只描述虚拟 mip k | `IndirectMap`、`Shaders/Core/IndirectMap.shader` |
| 物理页 | 每个启用层一张物理图，无硬件 mip，只采样 0 级 | `PhysicalLayers` |
| 烘焙 | 生产者把一页的各层画进 scratch | `IPageProducer` |
| 压缩 | 把 scratch 编码成块，写进物理槽（BC3 / ETC2 / ASTC） | `PageEncoder`、`AstcLayout`、`Shaders/Compress` |
| 着色采样 | lod、间接查找、物理 uv、过滤、层解码、PBR 光照 | `Shaders/Core/VirtualTextureCommon.hlsl`、`VirtualTextureLighting.hlsl` |

## 底层与生产者的边界

- 底层（`Runtime/Core`、`Shaders/Core`、`Shaders/Compress`）里不出现 `Terrain`。底层只认识虚拟 uv、PageId、物理槽和层。
- 生产者（`Runtime/Terrain`、`Shaders/Terrain`）只做两件事：
  - 地址计算：虚拟 uv 与自身空间互换；
  - 按烘焙单把各层画进底层给出的 scratch。
- 新的生产者（Decal、SVT 等）只新增生产者代码与地址计算。间接表、物理池、反馈解析、LRU、压缩不允许复制。

## 状态与并发

- 页状态、页到槽的映射、槽到页的反向映射各只有一套：
  - 页 → 槽：`PageDirectory`
  - 槽 → 页：`PhysicalPool` 槽上的 `owner`
- 共享的页状态和 LRU 只有 `RequestResolveJob` 一个写入者。
- 渲染 Pass 只消费已经完成的解析结果，录制 CommandBuffer 时不跑解析。
- 启动先通过同一条页请求通路烘焙根页；根页始终驻留并覆盖细页缺失。
- 根页的物理内容与间接表发布后，生产者在 Game 相机剔除前把完整位于体积内的地形切换为纯 VT 材质；其他相机和跨边界地形保持原始材质。
- VT 地形的前向、深度、阴影和反馈不声明原始 splat 图层，不使用 additive 或原生 basemap pass；源图层只在烘焙时采样。禁用后恢复原始材质和 basemap 距离。
- 地形烘焙以连续图层 UV 生成 stochastic 偏移；颜色、法线、mask 共用位置和权重，所有 SampleGrad 使用偏移前的 UV 梯度。

## 命名

- 不使用 F 前缀（类型、函数、字段都不用）。不引入任何新的类型前缀。
- 枚举保留 E 前缀。
- 与 Unity 自带类型重名时，换一个库内不带前缀的名字。
- 着色器全局名必须名副其实，统一使用 `_VT` 前缀。

## 不留兼容层

- 改名、删除时，仓库里的引用（代码、shader 名、`.asset` / `.mat` / `.prefab` 的 YAML）一次改完。
- 不留 `[Obsolete]`、旧名转发、兼容字段或旧 shader 名。
- 发现旧字段、未接入的演示或走不到的路径，直接删掉。

## 代码约定

- asmdef 划分固定：
  - `Runtime`、`Editor`、`Shaders`
  - `Tests/Editor`：EditMode 测试，受 `UNITY_INCLUDE_TESTS` 约束
- 热路径保持 Burst、原生内存（`UnsafeUtility.Malloc` / `NativeArray`）与 `ref` 访问的写法。
- 注释精简，只说"做什么 / 为什么"。
- 每个新文件和新目录都要提交手写的 `.meta`，GUID 为 32 位十六进制且全仓唯一。以 git 包安装时目录只读，Unity 不会补生成 `.meta`。
- 移动文件时连同 `.meta` 一起移动，保持 GUID 不变。

## 测试

- 测试只用内存中的模拟数据，覆盖：
  - 页状态
  - 唯一请求
  - 祖先链
  - LRU
  - 间接表 mip 的查找数学
  - 压缩块的打包尺寸
- 不把反馈图、间接表、物理页或压缩结果写成文件。
- 临时目录和临时文件用完即删。

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

EditMode 测试在 Unity 6 的 Test Runner 中运行（Infinity.Rendering.VirtualTexture.Tests）。
