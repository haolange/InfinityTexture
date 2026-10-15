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

        [Header("Physical")]
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

        public int mipCount => math.tzcnt(pageCount) + 1;
        /// <summary>一个物理槽的边长像素（含两侧 border）。</summary>
        public int slotTexels => pageTexels + 2 * (int)pageBorder;
        /// <summary>物理图边长像素。</summary>
        public int poolTexels => poolSize * slotTexels;

        /// <summary>参数是否能建出一套虚拟纹理；不能时给出原因。</summary>
        public bool Validate(out string error)
        {
            error = null;
            if (!math.ispow2(pageCount) || pageCount > PageId.MaxPageCount) { error = $"pageCount {pageCount} 必须是不超过 {PageId.MaxPageCount} 的 2 的幂"; }
            else if (poolSize > 255) { error = $"poolSize {poolSize} 超出间接表 8 位槽坐标"; }
            else if (indirectShader == null) { error = "缺少 IndirectMap shader 引用"; }
            return error == null;
        }

        void OnValidate()
        {
            pageCount = math.clamp(math.ceilpow2(pageCount), 128, 4096);
        }
    }
}
