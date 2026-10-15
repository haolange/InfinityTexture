using Unity.Mathematics;
using UnityEngine.Rendering;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>一次烘焙：把虚拟 uv 范围 uvRect（含 border）的内容画满当前绑定的 scratch。</summary>
    internal struct PageBakeRequest
    {
        public PageId page;
        /// <summary>xy 起点，zw 尺寸，虚拟 uv 空间。</summary>
        public float4 uvRect;
    }

    /// <summary>
    /// 页内容的生产者。底层负责选页、分槽、绑定 scratch 和编码；
    /// 生产者只做地址换算（虚拟 uv → 自身空间）并画出各启用层。
    /// </summary>
    internal interface IPageProducer
    {
        void Bake(CommandBuffer cmd, in PageBakeRequest request);
    }
}
