using Unity.Mathematics;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>
    /// 虚拟纹理的地址数学。着色器 VirtualTextureCommon.hlsl 用同样的公式，改动时两边一起改。
    /// </summary>
    internal static class VirtualTextureMath
    {
        /// <summary>page 在间接表第 level 级上覆盖的格子：xy 起点，zw 宽高。要求 level ≤ page.mip。</summary>
        public static int4 Footprint(in PageId page, int level)
        {
            int shift = page.mip - level;
            return new int4(page.x << shift, page.y << shift, 1 << shift, 1 << shift);
        }

        /// <summary>间接表 texel（RGBA8）：r/g 物理槽坐标，b 页实际所在 mip，a = 255 表示有效。</summary>
        public static uint IndirectValue(int2 slotCoord, int pageMip)
        {
            return (uint)slotCoord.x | (uint)slotCoord.y << 8 | (uint)pageMip << 16 | 0xFFu << 24;
        }

        /// <summary>page 在虚拟 uv 空间的范围（含两侧 border）：xy 起点，zw 尺寸。烘焙时生产者按它取内容。</summary>
        public static float4 PageUVRect(in PageId page, int pageCount, int pageTexels, int border)
        {
            float size = (float)(1 << page.mip) / pageCount;
            float borderSize = size * border / pageTexels;
            return new float4(page.x * size - borderSize, page.y * size - borderSize, size + 2 * borderSize, size + 2 * borderSize);
        }

        /// <summary>
        /// 虚拟 uv 落在 pageMip 级、物理槽 slotCoord 的那一页时，对应的物理像素坐标。
        /// 页内偏移用对齐前的 uv。
        /// </summary>
        public static float2 PhysicalTexel(float2 uv, int2 slotCoord, int pageMip, int pageCount, int pageTexels, int border)
        {
            float2 offset = math.frac(uv * (pageCount >> pageMip));
            return (float2)slotCoord * (pageTexels + 2 * border) + border + offset * pageTexels;
        }
    }
}
