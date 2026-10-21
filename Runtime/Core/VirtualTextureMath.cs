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
        /// 过滤使用的 lod（虚拟像素空间，dx / dy 为屏幕导数）。各向异性时 lod 取自长轴 / 查找次数，
        /// taps 为沿长轴的查找次数；其余过滤 taps 为 1。着色器 VTFilterLod 同式。
        /// </summary>
        public static float FilterLod(float2 dx, float2 dy, EPageFilter filter, int anisoSamples, out int taps)
        {
            taps = 1;
            float lengthX = math.length(dx);
            float lengthY = math.length(dy);
            float major = math.max(lengthX, lengthY);
            if (filter != EPageFilter.Anisotropic) { return math.log2(math.max(major, 1e-6f)); }

            float minor = math.max(math.min(lengthX, lengthY), 1e-6f);
            taps = (int)math.clamp(math.ceil(major / minor), 1, anisoSamples);
            return math.log2(math.max(major / taps, minor));
        }

        /// <summary>反馈请求的 mip：双线性取最近的一级，三线性与各向异性取 floor（ceil 级是父页，会随祖先一并入队）。</summary>
        public static int RequestMip(float lod, EPageFilter filter, int maxMip)
        {
            float level = filter == EPageFilter.Bilinear ? math.floor(lod + 0.5f) : math.floor(lod);
            return (int)math.clamp(level, 0, maxMip);
        }

        /// <summary>
        /// 虚拟 uv 落在 pageMip 级、物理槽 slotCoord 的那一页时，对应的物理像素坐标。
        /// 页内偏移用对齐前的 uv。
        /// </summary>
        public static float2 PhysicalTexel(float2 uv, int2 slotCoord, int pageMip, int pageCount, int pageTexels, int border)
        {
            uv = math.clamp(uv, 0, 1 - 1e-7f);
            float2 offset = math.frac(uv * (pageCount >> pageMip));
            return (float2)slotCoord * (pageTexels + 2 * border) + border + offset * pageTexels;
        }
    }
}
