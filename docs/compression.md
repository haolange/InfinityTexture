# 烘焙与压缩

物理图由 `PhysicalLayers` 持有，编码由 `PageEncoder` 和 `Shaders/Compress/PageEncode.compute` 完成。ASTC 的块配置只在 `AstcLayout` 里生成，着色器读取上传的参数和表。

## 层与物理格式

`EPageLayers` 决定分配哪些层。颜色层用 sRGB 物理格式，法线和 Mask 用线性格式。

| 压缩 | BaseColor | Normal | Mask | 块像素 |
|---|---|---|---|---|
| None | `R8G8B8A8_SRGB` | `R8G8B8A8_UNorm` | `R8G8B8A8_UNorm` | 1×1 |
| BC | `RGBA_DXT1_SRGB`（BC1） | `RG_BC5_UNorm`（BC5） | `RGBA_DXT5_UNorm`（BC3） | 4×4 |
| ETC2 | `RGB_ETC2_SRGB` | `RGBA_ETC2_UNorm` | `RGBA_ETC2_UNorm` | 4×4 |
| ASTC | 对应块的 `RGBA_ASTC*_SRGB` | 对应块的 `RGBA_ASTC*_UNorm` | 同 Normal | 4×4、6×6 或 8×8 |

ASTC 码率由 `EAstcBlock` 决定：4×4 为 8 bpp，6×6 约为 3.56 bpp，8×8 为 2 bpp。默认块是 6×6。

编码 kernel：BC 为 `EncodeBC1` / `EncodeBC5` / `EncodeBC3`；ETC2 颜色为 `EncodeETC2RGB`，数据为 `EncodeETC2`；ASTC 三层都是 `EncodeASTC`。

块图是一张 uint 纹理，一个纹素一块。BC 与 ETC2 的颜色层为 8 字节，`R32G32_UInt`。这两档的法线、Mask，以及全部 ASTC 层，为 16 字节，`R32G32B32A32_UInt`。None 不创建块图，scratch 直接 `CopyTexture` 到物理 RenderTexture。

## 通道

scratch 与 None 的物理格式相同：颜色 sRGB，数据线性，尺寸为整个槽（含 border）。地形烘焙写入法线时使用 G = y、A = x。

BC5 从 scratch 的 A、G 取出 x、y，各自按 BC4 编码进 R、G。采样时 `_VTNormalIsRG == 1` 把 RG 排回 G = y、A = x，再和 None、ETC2、ASTC 走同一个解码。ETC2 与 ASTC 不设置该标志，物理图保持 G = y、A = x。

压缩颜色 kernel 在写块前把线性值转到 sRGB。颜色物理格式本身是 sRGB，硬件采样在 Linear 工程里返回线性颜色。数据层不做这次转换。

ASTC 使用单分区、CEM 12（LDR RGBA 直接端点）。颜色层单平面。数据层双平面，A 通道单独一套权重，对应法线的 x 和 Mask 的粗糙度。权重网格、端点范围和块模式位由 `AstcLayout` 按块尺寸与是否颜色层决定，并上传查找表。位布局遵循 Khronos Data Format Specification 的 ASTC 章节。

## 工作集

`PageFormat.batchSize`：BC、ETC2、ASTC 4×4 为 4；None、ASTC 6×6、ASTC 8×8 为 1。`PhysicalLayers` 再与 `bakesPerRound` 取最小值，得到实际 `batchSize`。一轮烘焙单按这个宽度切块。

每一块的 GPU 顺序：

1. 逐页 `BindScratch`。该页各层 scratch 作为 MRT 清屏，生产者按 `PageUVRect` 画入。`PageUVRect` 在虚拟 uv 上把页矩形向两侧扩 `border / pageTexels`。
2. `Encode`。有压缩时，compute 读取 scratch、写入块图。
3. 复制。压缩路径按块数 `CopyTexture` 到 `slotCoord * slotTexels`。None 路径复制整张 scratch。

`batchSize > 1` 时，块图数量为 `batchSize × 层数`，索引为 `page * layerCount + layer`。这一块里的编码全部完成后再复制，避免后一次 dispatch 覆盖尚未复制的块。`batchSize == 1` 时只有一张共享块图，每一层 dispatch 之后立即复制，再给下一层使用。

Bake 当帧，物理图、scratch 和块图都导入 RenderGraph。Bake pass 声明读写。同一帧随后的 Indirect pass 把物理图声明为读。需要更新时才重写间接表，并在 pass 末尾 `Publish`。

## 创建失败

`VirtualTextureAsset.Validate` 拒绝非法枚举、非 2 的幂或过大的 `pageCount`、非正的请求参数、槽边长未按块对齐，以及缺少 IndirectMap 或（压缩时）PageEncode。

`VirtualTextureSystem.CanCreate` 在此之后检查：

- `poolTexels` 与 `pageCount` 不超过 `SystemInfo.maxTextureSize`
- 支持异步回读，且 `R32_UInt` 可以渲染
- 每个启用层的物理格式可以采样，对应 scratch 格式可以渲染
- 压缩时块图格式可以 Load/Store，平台支持 compute shader，并且 `copyTextureSupport` 同时包含 `DifferentTypes` 与 `RTToTexture`

任一条件失败都返回错误字符串。地形组件记录错误后不创建系统，不更换压缩格式，地形保持原材质。编码 dispatch 本身没有另一条失败回退。
