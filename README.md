# Infinity Texture

`com.infinity.virtual-texture` 在 Unity 6 的 Universal Render Pipeline 上，为完整位于体积内的 Terrain 维护一套运行时虚拟纹理。包版本 1.0.0，许可 [Mozilla Public License 2.0](LICENSE)。命名空间为 `Landscape.RuntimeVirtualTexture`。显示名为 Infinity Texture。

Play 模式下，Game 相机把可见页写入反馈图。回读到达后，请求通路去重、补祖先并分配物理槽，再把页烘焙进物理图、更新间接表。地形着色通过间接表定位物理槽，在物理图的 0 级上过滤。同一时间只有一个 `VirtualTextureSystem`。底层只处理虚拟 uv、页、槽和层；当前生产者是地形。

## 依赖

`package.json` 声明的宿主版本：

| 包 | 版本 |
|---|---|
| Unity | 6000.0 |
| Universal Render Pipeline | 17.0.3 |
| Burst | 1.8.12 |
| Collections | 2.4.3 |
| Mathematics | 1.3.2 |

渲染入口是 `ScriptableRenderPass.RecordRenderGraph`。2026-10-22 的一次测量使用 Unity 6000.6.0f1、URP 17.6.0、Apple M3 Max 与 Metal。测量范围、数据和未覆盖平台见 [docs/validation.md](docs/validation.md)。声明版本与该次测量版本分开阅读。

## 接入

完整步骤、体积规则和参数表见 [docs/usage.md](docs/usage.md)。

1. 用菜单 `Landscape/VirtualTextureAsset` 创建资产，或直接使用 `Runtime/Profile/RuntimeVirtualTexture.asset`。新建资产会带上 IndirectMap 着色器与 PageEncode compute 的默认引用。
2. 在场景中添加 `TerrainVirtualTexture`，设置体积边长和资产。组件默认引用示例材质 `M_TerrainLit-VT` 与烘焙着色器 `Hidden/VirtualTexture/TerrainBake`。
3. 把 `VirtualTextureFeature` 加到 Game 相机使用的 URP Renderer Data。反馈只绘制该 Feature 的 `layerMask` 中、着色器含 `LightMode = VTFeedback` 的不透明物体。包内 `Runtime/Profile/PipelineSettings` 有一份示例 Renderer 与管线资产。
4. 进入 Play。根页沿同一条请求通路烘焙。根页内容和间接表发布之后，完整落在体积内的地形会在 Game 相机开始渲染前换上虚拟纹理材质。

`CanCreate` 失败、体积内没有完整包含的地形，或已经存在另一套系统时，不创建运行时，地形保持原材质。

## 文档

| 文档 | 内容 |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | 模块、帧序、驻留、间接表、生产者边界 |
| [docs/usage.md](docs/usage.md) | 接入、体积、可调参数 |
| [docs/sampling.md](docs/sampling.md) | 查找、过滤、时间 mip、反馈、层解码 |
| [docs/compression.md](docs/compression.md) | 物理格式、烘焙工作集、编码与复制 |
| [docs/validation.md](docs/validation.md) | 2026-10-22 测量记录 |
| [AGENTS.md](AGENTS.md) | 修改本库时必须遵守的约束 |

结构以当前源码为准。测量记录只描述那一次运行。
