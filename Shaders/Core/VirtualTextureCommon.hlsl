#ifndef VIRTUAL_TEXTURE_COMMON_INCLUDED
#define VIRTUAL_TEXTURE_COMMON_INCLUDED

// 底层着色器公共部分：只认识虚拟 uv、PageId 和物理槽，不依赖任何生产者。

// x: 第 0 级每边页数  y: mip 级数  z: 每页有效像素  w: 每页单边 border 像素
float4 _VTParams;
// x: 每个物理槽的像素（含两侧 border）  y: 1 / 物理图边长像素
float4 _VTPoolParams;
// 反馈图的实际缩小倍数：渲染尺寸 / 反馈尺寸（逐轴）
float2 _VTFeedbackScale;

// 启用的物理层数：1 BaseColor，2 +Normal，3 +Mask
int _VTLayerCount;
// x: 过滤方式（0 双线性，1 三线性，2 各向异性，对应 C# EPageFilter）  y: 各向异性最多查找次数
float4 _VTFilterParams;

// 间接表：第 k 级的 texel = (槽 x, 槽 y, 页所在 mip, 有效) / 255
TEXTURE2D(_VTIndirectMap);
// 物理图，通道布局见 C# EPageLayer
TEXTURE2D(_VTPhysicalBaseColor);
TEXTURE2D(_VTPhysicalNormal);
TEXTURE2D(_VTPhysicalMask);
SAMPLER(sampler_VTPointClamp);
SAMPLER(sampler_VTLinearClamp);

#define VT_PAGE_COUNT   ((uint)_VTParams.x)
#define VT_MAX_MIP      ((uint)_VTParams.y - 1)
#define VT_PAGE_TEXELS  (_VTParams.z)
#define VT_BORDER       (_VTParams.w)
#define VT_TEXEL_SCALE  (VT_PAGE_COUNT * VT_PAGE_TEXELS)
#define VT_FILTER       ((uint)_VTFilterParams.x)
#define VT_BILINEAR     0
#define VT_TRILINEAR    1
#define VT_ANISOTROPIC  2

// 过滤使用的 lod：dx / dy 为虚拟像素坐标的屏幕导数，0 级时一个屏幕像素对应一个虚拟像素。
// 各向异性时 lod 取自 长轴 / 查找次数，taps 为沿长轴的查找次数。与 C# VirtualTextureMath.FilterLod 同式。
float VTFilterLod(float2 dx, float2 dy, out uint taps)
{
    taps = 1;
    float lengthX = length(dx);
    float lengthY = length(dy);
    float major = max(lengthX, lengthY);
    if (VT_FILTER != VT_ANISOTROPIC) { return log2(max(major, 1e-6)); }

    float minor = max(min(lengthX, lengthY), 1e-6);
    taps = (uint)clamp(ceil(major / minor), 1.0, _VTFilterParams.y);
    return log2(max(major / taps, minor));
}

// 反馈请求的 mip：双线性取最近一级；三线性与各向异性取 floor，ceil 级是父页，会随祖先一并入队。
uint VTRequestMip(float lod)
{
    float level = VT_FILTER == VT_BILINEAR ? floor(lod + 0.5) : floor(lod);
    return (uint)clamp(level, 0.0, (float)VT_MAX_MIP);
}

struct VTAddress
{
    float2 physicalUV;
    uint3 resident;
    bool valid;
};

// 在间接表第 mip 级点采样一次，得到"自身或最近驻留祖先"所在的物理槽。
// 页内偏移用对齐前的 uv，按页实际所在的 mip 计算。与 C# VirtualTextureMath.PhysicalTexel 同式。
VTAddress VTLookup(float2 uv, uint mip)
{
    uv = clamp(uv, 0.0, 1.0 - 1e-7);
    float4 entry = SAMPLE_TEXTURE2D_LOD(_VTIndirectMap, sampler_VTPointClamp, uv, mip) * 255.0;
    uint pageMip = (uint)round(entry.b);
    float2 offset = frac(uv * (VT_PAGE_COUNT >> pageMip));
    float2 texel = round(entry.rg) * _VTPoolParams.x + VT_BORDER + offset * VT_PAGE_TEXELS;

    VTAddress address;
    address.physicalUV = texel * _VTPoolParams.y;
    address.resident = uint3((uint2)round(entry.rg), pageMip);
    address.valid = entry.a > 127.5;
    return address;
}

// 一次查找读到的各层原始 texel。过滤在这一层累加，最后统一解码。
struct VTTexels
{
    float4 baseColor;
    float4 normal;
    float4 mask;
};

// 未启用的层用的常量：平法线；金属度 0、AO 1、高光 0.5、粗糙度 1。
#define VT_FLAT_NORMAL   float4(0.0, 0.5, 0.0, 0.5)
#define VT_DEFAULT_MASK  float4(0.0, 1.0, 0.5, 1.0)

VTTexels VTSampleTexels(float2 physicalUV)
{
    VTTexels texels;
    texels.baseColor = SAMPLE_TEXTURE2D_LOD(_VTPhysicalBaseColor, sampler_VTLinearClamp, physicalUV, 0);
    texels.normal = VT_FLAT_NORMAL;
    texels.mask = VT_DEFAULT_MASK;
    if (_VTLayerCount > 1) { texels.normal = SAMPLE_TEXTURE2D_LOD(_VTPhysicalNormal, sampler_VTLinearClamp, physicalUV, 0); }
    if (_VTLayerCount > 2) { texels.mask = SAMPLE_TEXTURE2D_LOD(_VTPhysicalMask, sampler_VTLinearClamp, physicalUV, 0); }
    return texels;
}

struct VTSurface
{
    half3 baseColor;
    half3 normalTS;
    half metallic;
    half occlusion;
    half specular;
    half roughness;
};

VTSurface VTDecodeSurface(VTTexels texels)
{
    VTSurface surface;
    surface.baseColor = texels.baseColor.rgb;
    float2 normalXY = float2(texels.normal.a, texels.normal.g) * 2.0 - 1.0;
    surface.normalTS = half3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));
    surface.metallic = texels.mask.r;
    surface.occlusion = texels.mask.g;
    surface.specular = texels.mask.b;
    surface.roughness = texels.mask.a;
    return surface;
}

VTTexels VTLerpTexels(VTTexels a, VTTexels b, float t)
{
    VTTexels result;
    result.baseColor = lerp(a.baseColor, b.baseColor, t);
    result.normal = lerp(a.normal, b.normal, t);
    result.mask = lerp(a.mask, b.mask, t);
    return result;
}

// 一次查找：间接表第 mip 级点采样 + 物理图 0 级双线性。
VTTexels VTBilinear(float2 uv, uint mip, inout bool valid)
{
    VTAddress address = VTLookup(uv, mip);
    valid = valid && address.valid;
    return VTSampleTexels(address.physicalUV);
}

// floor(lod) 与 ceil(lod) 两级各查一次再插值。
VTTexels VTTrilinear(float2 uv, float lod, inout bool valid)
{
    float level = clamp(lod, 0.0, (float)VT_MAX_MIP);
    uint fine = (uint)floor(level);
    uint coarse = (uint)ceil(level);
    VTAddress a = VTLookup(uv, fine);
    valid = valid && a.valid;
    VTTexels result = VTSampleTexels(a.physicalUV);
    if (fine == coarse) { return result; }

    VTAddress b = VTLookup(uv, coarse);
    valid = valid && b.valid;
    // 同一个 uv 落到同一驻留页时，物理坐标也相同。
    if (all(a.resident == b.resident)) { return result; }
    return VTLerpTexels(result, VTSampleTexels(b.physicalUV), level - fine);
}

// 沿足迹长轴均匀取 taps 个点，每点做同样的三线性查找后平均。
VTTexels VTAnisotropic(float2 uv, float2 dx, float2 dy, float lod, uint taps, inout bool valid)
{
    float2 majorAxis = (dot(dx, dx) > dot(dy, dy) ? dx : dy) / VT_TEXEL_SCALE;
    VTTexels sum = (VTTexels)0;
    for (uint i = 0; i < taps; ++i)
    {
        float2 offset = majorAxis * ((i + 0.5) / taps - 0.5);
        sum = VTLerpTexels(sum, VTTrilinear(uv + offset, lod, valid), 1.0 / (i + 1));
    }
    return sum;
}

// 着色采样：dx / dy 为虚拟像素坐标（uv × VT_TEXEL_SCALE）的屏幕导数，需在分支外求得。
// 返回 false 表示该处还没有任何驻留页（连根页都没有）。
bool VTSampleSurface(float2 uv, float2 dx, float2 dy, out VTSurface surface)
{
    uint taps;
    float lod = VTFilterLod(dx, dy, taps);
    bool valid = true;

    VTTexels texels;
    if (VT_FILTER == VT_BILINEAR) { texels = VTBilinear(uv, VTRequestMip(lod), valid); }
    else if (VT_FILTER == VT_TRILINEAR) { texels = VTTrilinear(uv, lod, valid); }
    else { texels = VTAnisotropic(uv, dx, dy, lod, taps, valid); }

    surface = VTDecodeSurface(texels);
    return valid;
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

    float2 texels = uv * VT_TEXEL_SCALE;
    uint taps;
    float lod = VTFilterLod(ddx(texels) / _VTFeedbackScale.x, ddy(texels) / _VTFeedbackScale.y, taps);
    uint mip = VTRequestMip(lod);
    uint pagesAtMip = VT_PAGE_COUNT >> mip;
    uint2 page = min((uint2)floor(uv * pagesAtMip), pagesAtMip - 1);
    return VTPackPageId(page, mip);
}

#endif
