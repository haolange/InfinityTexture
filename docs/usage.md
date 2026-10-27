# 接入与参数

本文说明如何把包放进一个 URP 场景。运行时对象之间的关系见 [ARCHITECTURE.md](../ARCHITECTURE.md)。

## 场景组成

需要三样东西同时存在：

1. 一份 `VirtualTextureAsset`。
2. 场景里的一个 `TerrainVirtualTexture`。
3. Game 相机所用 Renderer Data 上的一个 `VirtualTextureFeature`。

`TerrainVirtualTexture` 的脚本导入器带有默认引用：`Runtime/Profile/RuntimeVirtualTexture.asset`、材质 `Runtime/Material/M_TerrainLit-VT`、烘焙着色器 `Hidden/VirtualTexture/TerrainBake`。`VirtualTextureAsset` 的导入器带有 `Hidden/VirtualTexture/IndirectMap` 和 `Shaders/Compress/PageEncode.compute`。菜单 `Landscape/VirtualTextureAsset` 创建的新资产会得到这两项 shader 引用。检查器里它们是隐藏字段；`Validate` 发现缺失时会失败。

组件执行顺序为 100。`OnEnable` 时若资产、材质或烘焙着色器缺失，或 `VirtualTextureSystem.CanCreate` 失败，或已经有另一套 active 系统，则记录错误并保持地形原材质。没有地形完整落在体积内时，同样不创建系统。

## 体积

`volumeSize` 取 `EVirtualTextureVolumeSize`：128、256、512、1024、2048、4096，默认 1024。体积是 XZ 平面上的正方形。中心由 Transform 的 x、z 按半个边长取整：

```text
half = size * 0.5
center = floor(position / half + 0.5) * half
rect = (center - half, size, size)
```

检查器把 Transform 的 `localScale.x` 和 `localScale.z` 写成这个边长，场景里的线框画在 Transform 的位置上。运行时体积中心仍按上面的半边长取整，未落在格点上的 Transform 位置与实际矩形中心可以不同。

启用时遍历 `Terrain.activeTerrains`。地形位置与 `terrainData.size` 组成的 XZ 矩形必须完全落在体积内，才会进入生产者。只发生重叠时输出警告，该地形继续使用原来的材质。启用之后才加载或移入体积的地形不会补进列表，需要重新启用组件。

`_VTVolumeRect` 在启用时写入：xy 为矩形原点，zw 为宽高。世界坐标到虚拟 uv 是 `(positionWS.xz - xy) / zw`。

## 材质切换

根页烘焙并 `Publish` 之后，`rootReady` 为 true。此后每个相机开始渲染时：

- Game 相机，且这套系统仍是 active 系统：已收集地形的 `materialTemplate` 换成 `Landscape/TerrainLit_VT`，`basemapDistance` 设为 `float.MaxValue`。
- 其他相机，或根页尚未发布：若当前已经换上虚拟纹理材质，则恢复启用时备份的材质和 basemap 距离。

备份时如果地形已经引用虚拟纹理材质，会先换成当前 Render Pipeline 的默认地形材质再记录。禁用组件时释放系统并恢复。

虚拟纹理材质含 `VTFeedback`、`UniversalForward`、`ShadowCaster`、`DepthOnly` 和 `SceneSelectionPass`。这些 pass 不采样 splat。图层贴图只由烘焙着色器读取。

## Renderer Feature

`VirtualTextureFeature.layerMask` 默认全部层。反馈使用不透明队列，并要求着色器标签 `LightMode = VTFeedback`。示例 `PlayerRenderer` 把 mask 设成第 3 层；使用该示例时，参与反馈的地形物体需要放在这一层。代码默认的全层 mask 则按物体自身的层过滤。

Feature 在 `BeforeRenderingPrePasses` 入队。pass 只在 Play 模式、存在 active 系统、且相机类型为 Game 时录制。

## 资产参数

检查器范围来自字段上的 `Range`。`Validate` 还会拒绝非 2 的幂的 `pageCount`、超过 `PageId.MaxPageCount`（8192）的 `pageCount`、未对齐的槽边长，以及缺失的 shader。`OnValidate` 把 `pageCount` 收到 128–4096 的 2 的幂。`poolSize` 的检查器范围是 4–64，`Validate` 接受 1–255；槽索引用 `ushort` 保存。

| 字段 | 默认 | 检查器范围 | 含义 |
|---|---|---|---|
| `pageCount` | 2048 | 128–4096，2 的幂 | 第 0 级每边页数。`mipCount = tzcnt(pageCount) + 1` |
| `pageTexels` | 256 | 64–512 | 每页有效像素，不含 border |
| `pageBorder` | `X4`（4） | 1、2、4 | 槽单边 border，服务一次双线性 |
| `filter` | `Trilinear` | Bilinear / Trilinear / Anisotropic | 着色过滤 |
| `temporalMip` | false | — | 在已确认的 TAA 相机上于相邻 mip 间选择 |
| `anisoSamples` | 4 | 2–16 | 各向异性沿长轴的查找次数上限 |
| `layers` | `BaseColorNormalMask` | 一至三层 | 未启用的层不分配物理图 |
| `compression` | `None` | None / BC / ETC2 / ASTC | 物理图存储 |
| `astcBlock` | `X6` | 4×4 / 6×6 / 8×8 | 只在 ASTC 时生效 |
| `poolSize` | 16 | 4–64 | 物理图每边槽数 |
| `bakesPerRound` | 8 | 1–64 | 每轮最多新烘焙的页数，并参与工作集上限 |
| `queueCapacity` | 512 | 16–4096 | 排队页上限 |
| `queueTimeout` | 30 | 1–600 | 排队页连续未被请求的轮数上限，超过后回到空闲 |

派生尺寸：

```text
slotTexels = pageTexels + 2 * pageBorder
poolTexels = poolSize * slotTexels
```

压缩时 `slotTexels` 必须是块宽高的整数倍。物理图边长还不能超过 `SystemInfo.maxTextureSize`。

层的含义：`BaseColor` 为 sRGB 底色；`Normal` 为切线空间法线；`Mask` 的 R、G、B、A 分别是金属度、AO、高光、粗糙度。过滤和压缩的行为见 [sampling.md](sampling.md) 与 [compression.md](compression.md)。
