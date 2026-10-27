# 采样与反馈

地址公式以 `VirtualTextureMath` 与 `Shaders/Core/VirtualTextureCommon.hlsl` 为准，两边同式。物理图只在 0 级用 `sampler_VTLinearClamp` 采样。

## 导数与 lod

生产者把虚拟 uv 乘上 `VT_TEXEL_SCALE`（`pageCount × pageTexels`）后取屏幕导数，交给 `VTSampleSurface`。0 级下，一个屏幕像素对应一个虚拟像素。

记导数长度的长轴为 `major`、短轴为 `minor`：

- Bilinear 与 Trilinear：`lod = log2(major)`，查找次数为 1。
- Anisotropic：`taps = clamp(ceil(major / minor), 1, anisoSamples)`，`lod = log2(max(major / taps, minor))`。

lod 再限制到 `[0, mipCount - 1]`。

## 三种过滤

`_VTFilterParams.x` 等于 `EPageFilter` 的序号：0 双线性，1 三线性，2 各向异性。

**双线性。** `VTRequestMip` 取 `floor(lod + 0.5)`。间接表在该级 Load 一次，物理图在 0 级采样一次。

**三线性。** 在 `floor(lod)` 与 `ceil(lod)` 各 Load 一次。两级落到同一驻留页时物理坐标相同，只采样一次。落到不同驻留页时再采样第二次，并在 texel 空间按 lod 的小数部分插值。

**各向异性。** 沿长轴均匀取 `taps` 个 uv，每个点做上面的三线性，然后递推平均。整次调用共用进入函数时算出的那一个 lod。

过滤在编码前的 texel 上插值，最后才 `VTDecodeSurface`。

## 间接查找

`VTLookup(uv, mip)` 把 uv 限制在 `[0, 1)`，按该级边长 `pageCount >> mip` 得到整数格子，再 `LOAD_TEXTURE2D_LOD`。还原后的纹素：

| 通道 | 内容 |
|---|---|
| R、G | 物理槽坐标 |
| B | 页的实际 mip |
| A | 255 为有效；读回值大于 127.5 才采用 |

页内偏移使用对齐前的 uv，并按页的实际 mip 而不是查找 mip 取小数部分：

```text
offset = frac(uv * (pageCount >> pageMip))
texel  = slot * slotTexels + border + offset * pageTexels
physicalUV = texel / poolTexels
```

格子上写的是自身或最近驻留祖先，着色器不再沿 mip 循环。

## 时间 mip

`VirtualTextureAsset.temporalMip` 默认为 false。Indirect pass 同时满足下面条件才把 `_VTTemporalEnabled` 置 1：

- 资产打开 `temporalMip`，且过滤不是 Bilinear
- `rootReady` 已发布
- 相机类型为 Game，渲染器是 `UniversalRenderer`，不是立体相机
- 抗锯齿为 TAA，后处理打开，MSAA 为 1，未开动态分辨率
- `UniversalAdditionalCameraData` 存在，渲染类型为 Base，`resetHistory` 为 false，`cameraStack` 为空
- TAA history 存在，积累纹理非空，积累版本等于 `Time.frameCount - 1`
- 当前投影相对 `nonJitteredProjectionMatrix` 只有像素级抖动（`VirtualTextureMath.IsPixelJitter`）

打开后，三线性用片元坐标和 `_VTTemporalSeed`（`frameCount * 747796405`）做哈希，按 lod 的小数部分选择 fine 或 coarse，并把另一次查找收成同一级。各向异性的每个 tap 传入同一次哈希，因此一片元仍是一次 mip 选择。双线性路径不读这个开关。

反馈函数 `VTFeedback` 不读取 `_VTTemporalEnabled`。

## 反馈

反馈图每轴尺寸为 `max(1, ceil(renderSize / 16))`，颜色 `R32_UInt`，深度 `D16_UNorm`。`_VTFeedbackScale` 是渲染尺寸除以反馈尺寸，用于把反馈图上的导数换回渲染分辨率。

绘制前把视图投影设成去掉抖动的矩阵，画完再恢复相机自己的投影。像素超出虚拟 uv 的 `[0, 1)` 时写入 0。否则用与着色相同的 `VTFilterLod`，再按 `VTRequestMip` 取一个 mip：双线性为最近一级，三线性与各向异性为 `floor(lod)`。ceil 那一级是父页，由 CPU 沿祖先链补上。打包格式与 `PageId` 相同。

`FeedbackReadback` 同一时刻只接受一个 `RequestAsyncGPUReadback`。回调若已取消、出错或尚未完成，就不调用 `Schedule`。多个 Game 相机在 `ReserveReadback` 里轮转：请求通路忙、回读在途，或还没轮到该相机时，这一帧不提交。系统销毁时把该次回读标记为取消，迟到的回调不再进入请求通路。

## 层解码

| 层 | 采样约定 |
|---|---|
| BaseColor | 物理格式为 sRGB。解码使用采样结果的 rgb |
| Normal | 统一按 A = x、G = y，映射到 `[-1, 1]` 后重建 z。BC 压缩时物理图是 RG，`_VTNormalIsRG` 为 1，采样后先重排成 G = y、A = x |
| Mask | R 金属度，G AO，B 高光，A 粗糙度 |

未启用的法线使用 `(0, 0.5, 0, 0.5)`，未启用的 Mask 使用 `(0, 1, 0.5, 1)`。`VTFragmentPBR` 用 `0.08 * specular` 作为电介质 F0，再按金属度组装 BRDF。

## 全局参数

系统构造时写入，或由对应 pass 按相机更新：

| 名称 | 含义 |
|---|---|
| `_VTParams` | x 页数，y mip 级数，z 每页有效像素，w border |
| `_VTPoolParams` | x 槽边长像素，y `1 / poolTexels` |
| `_VTLayerCount` | 1–3 |
| `_VTNormalIsRG` | BC 为 1，其余为 0 |
| `_VTIndirectMap` | 间接表 |
| `_VTPhysicalBaseColor` / `_VTPhysicalNormal` / `_VTPhysicalMask` | 各层物理图 |
| `_VTFilterParams` | x 过滤枚举，y `anisoSamples` |
| `_VTTemporalEnabled` / `_VTTemporalSeed` | 时间 mip |
| `_VTFeedbackScale` | 反馈相对渲染分辨率的逐轴倍数 |
| `_VTVolumeRect` | 地形生产者的世界 XZ 矩形 |

间接表写入 pass 使用材质属性 `_IndirectWrites`、`_IndirectWriteBase`、`_IndirectLevelSize`，它们不是全局 `_VT` 参数。
