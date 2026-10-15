#ifndef VIRTUAL_TEXTURE_COMMON_INCLUDED
#define VIRTUAL_TEXTURE_COMMON_INCLUDED

// 底层着色器公共部分：只认识虚拟 uv、PageId 和物理槽，不依赖任何生产者。

// x: 第 0 级每边页数  y: mip 级数  z: 每页有效像素  w: 每页单边 border 像素
float4 _VTParams;
// 反馈图的实际缩小倍数：渲染尺寸 / 反馈尺寸（逐轴）
float2 _VTFeedbackScale;

#define VT_PAGE_COUNT   ((uint)_VTParams.x)
#define VT_MAX_MIP      ((uint)_VTParams.y - 1)
#define VT_PAGE_TEXELS  (_VTParams.z)

// 虚拟像素空间里的 lod：0 级时一个屏幕像素对应一个虚拟像素。
float VTComputeLod(float2 dx, float2 dy)
{
    return 0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-12));
}

uint VTRequestMip(float lod)
{
    return (uint)clamp(floor(lod), 0.0, (float)VT_MAX_MIP);
}

// 与 C# PageId 相同的位布局：[31] 有效 | [30:26] mip | [25:13] y | [12:0] x
uint VTPackPageId(uint2 page, uint mip)
{
    return 0x80000000u | (mip << 26) | (page.y << 13) | page.x;
}

// 反馈：在缩小 _VTFeedbackScale 倍的图上求导，再换算回渲染分辨率的导数。
uint VTFeedback(float2 uv)
{
    if (any(uv < 0.0) || any(uv >= 1.0)) { return 0; }

    float2 texels = uv * (VT_PAGE_COUNT * VT_PAGE_TEXELS);
    float lod = VTComputeLod(ddx(texels) / _VTFeedbackScale.x, ddy(texels) / _VTFeedbackScale.y);
    uint mip = VTRequestMip(lod);
    uint pagesAtMip = VT_PAGE_COUNT >> mip;
    uint2 page = min((uint2)floor(uv * pagesAtMip), pagesAtMip - 1);
    return VTPackPageId(page, mip);
}

#endif
