#ifndef VIRTUAL_TEXTURE_COMMON_INCLUDED
#define VIRTUAL_TEXTURE_COMMON_INCLUDED

// 底层着色器公共部分：只认识虚拟 uv、PageId 和物理槽，不依赖任何生产者。

// x: 第 0 级每边页数  y: mip 级数  z: 每页有效像素  w: 每页单边 border 像素
float4 _VTParams;
// x: 每个物理槽的像素（含两侧 border）  y: 1 / 物理图边长像素
float4 _VTPoolParams;
// 反馈图的实际缩小倍数：渲染尺寸 / 反馈尺寸（逐轴）
float2 _VTFeedbackScale;

// 间接表：第 k 级的 texel = (槽 x, 槽 y, 页所在 mip, 有效) / 255
TEXTURE2D(_VTIndirectMap);
SAMPLER(sampler_VTPointClamp);
SAMPLER(sampler_VTLinearClamp);

#define VT_PAGE_COUNT   ((uint)_VTParams.x)
#define VT_MAX_MIP      ((uint)_VTParams.y - 1)
#define VT_PAGE_TEXELS  (_VTParams.z)
#define VT_BORDER       (_VTParams.w)

// 虚拟像素空间里的 lod：0 级时一个屏幕像素对应一个虚拟像素。
float VTComputeLod(float2 dx, float2 dy)
{
    return 0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-12));
}

uint VTRequestMip(float lod)
{
    return (uint)clamp(floor(lod), 0.0, (float)VT_MAX_MIP);
}

struct VTAddress
{
    float2 physicalUV;
    bool valid;
};

// 在间接表第 mip 级点采样一次，得到"自身或最近驻留祖先"所在的物理槽。
// 页内偏移用对齐前的 uv，按页实际所在的 mip 计算。与 C# VirtualTextureMath.PhysicalTexel 同式。
VTAddress VTLookup(float2 uv, uint mip)
{
    float4 entry = SAMPLE_TEXTURE2D_LOD(_VTIndirectMap, sampler_VTPointClamp, uv, mip) * 255.0;
    uint pageMip = (uint)round(entry.b);
    float2 offset = frac(uv * (VT_PAGE_COUNT >> pageMip));
    float2 texel = round(entry.rg) * _VTPoolParams.x + VT_BORDER + offset * VT_PAGE_TEXELS;

    VTAddress address;
    address.physicalUV = texel * _VTPoolParams.y;
    address.valid = entry.a > 127.5;
    return address;
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
