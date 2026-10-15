using UnityEngine;
using Unity.Mathematics;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>每页单边 border 像素：只服务于物理图上的一次双线性过滤。</summary>
    public enum EBorder
    {
        X1 = 1,
        X2 = 2,
        X4 = 4
    }

    /// <summary>
    /// 启用的 PBR 层，从少到多。每层一张物理图，未启用的层不分配。
    /// BaseColor：底色（sRGB）。Normal：切线空间法线。Mask：金属度、AO、高光、粗糙度。
    /// </summary>
    public enum EPageLayers
    {
        BaseColor,
        BaseColorNormal,
        BaseColorNormalMask
    }

    /// <summary>
    /// 着色时的过滤方式。物理图只有 0 级，三档都靠间接表的多级查找实现：
    /// Bilinear 一次查找；Trilinear 在 floor / ceil 两级各查一次再插值；
    /// Anisotropic 沿足迹长轴做 anisoSamples 次三线性查找再平均。
    /// </summary>
    public enum EPageFilter
    {
        Bilinear,
        Trilinear,
        Anisotropic
    }

    /// <summary>虚拟纹理的设置。只保存参数，运行时资源由 VirtualTextureSystem 创建。</summary>
    [CreateAssetMenu(menuName = "Landscape/VirtualTextureAsset")]
    public class VirtualTextureAsset : ScriptableObject
    {
        [Header("Virtual")]
        [Tooltip("第 0 级每边页数，2 的幂")]
        [Range(128, 4096)]
        public int pageCount = 2048;
        [Tooltip("每页有效像素（不含 border）")]
        [Range(64, 512)]
        public int pageTexels = 256;
        public EBorder pageBorder = EBorder.X4;

        [Header("Sampling")]
        public EPageFilter filter = EPageFilter.Trilinear;
        [Tooltip("各向异性时沿长轴的最多查找次数")]
        [Range(2, 16)]
        public int anisoSamples = 4;

        [Header("Physical")]
        public EPageLayers layers = EPageLayers.BaseColorNormalMask;
        public EPageCompression compression = EPageCompression.None;
        [Tooltip("物理池每边槽数")]
        [Range(4, 64)]
        public int poolSize = 16;

        [Header("Request")]
        [Tooltip("每轮反馈最多烘焙的页数")]
        [Range(1, 64)]
        public int bakesPerRound = 8;
        [Tooltip("排队页的上限")]
        [Range(16, 4096)]
        public int queueCapacity = 512;
        [Tooltip("排队页连续多少轮没被请求就退回空闲")]
        [Range(1, 600)]
        public int queueTimeout = 30;

        [SerializeField, HideInInspector]
        internal Shader indirectShader;
        [SerializeField, HideInInspector]
        internal ComputeShader encodeShader;

        public int mipCount => math.tzcnt(pageCount) + 1;
        /// <summary>一个物理槽的边长像素（含两侧 border）。</summary>
        public int slotTexels => pageTexels + 2 * (int)pageBorder;
        /// <summary>物理图边长像素。</summary>
        public int poolTexels => poolSize * slotTexels;
        internal PageFormat format => PageFormat.Of(compression);

        /// <summary>参数是否能建出一套虚拟纹理；不能时给出原因。</summary>
        public bool Validate(out string error)
        {
            error = null;
            if (!math.ispow2(pageCount) || pageCount > PageId.MaxPageCount) { error = $"pageCount {pageCount} 必须是不超过 {PageId.MaxPageCount} 的 2 的幂"; }
            else if (poolSize > 255) { error = $"poolSize {poolSize} 超出间接表 8 位槽坐标"; }
            else if (!format.IsAligned(slotTexels)) { error = $"槽边长 {slotTexels}（pageTexels + 2 × pageBorder）不是 {compression} 块宽 {format.blockSize.x} 的整数倍"; }
            else if (indirectShader == null) { error = "缺少 IndirectMap shader 引用"; }
            else if (format.isCompressed && encodeShader == null) { error = "缺少 PageEncode compute 引用"; }
            return error == null;
        }

        void OnValidate()
        {
            pageCount = math.clamp(math.ceilpow2(pageCount), 128, 4096);
        }
    }
}
