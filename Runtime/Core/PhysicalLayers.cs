using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>
    /// 物理图的一层。颜色层按 sRGB 存，数据层线性存。通道布局：
    /// BaseColor  rgb 底色
    /// Normal     g = 法线 y，a = 法线 x（编码到 0..1，z 在着色时重建）
    /// Mask       r 金属度，g AO，b 高光，a 粗糙度
    /// </summary>
    internal enum EPageLayer
    {
        BaseColor,
        Normal,
        Mask
    }

    /// <summary>
    /// 物理页通路：每个启用层一张物理图，外加一张一页大小的 scratch。
    /// 物理图不生成 mip，着色器只采样 0 级；border 只服务于这一次双线性。
    /// 烘焙时生产者画进 scratch，再由 Encode 写进目标槽。
    /// </summary>
    internal class PhysicalLayers : IDisposable
    {
        class Layer
        {
            public EPageLayer kind;
            public RenderTexture physical;
            public RenderTexture scratch;
        }

        static readonly int[] s_GlobalIDs =
        {
            Shader.PropertyToID("_VTPhysicalBaseColor"),
            Shader.PropertyToID("_VTPhysicalNormal"),
            Shader.PropertyToID("_VTPhysicalMask")
        };

        public static bool IsColor(EPageLayer layer) => layer == EPageLayer.BaseColor;

        /// <summary>预设启用的层，按 MRT 顺序。</summary>
        public static EPageLayer[] Enabled(EPageLayers preset)
        {
            switch (preset)
            {
                case EPageLayers.BaseColor: return new[] { EPageLayer.BaseColor };
                case EPageLayers.BaseColorNormal: return new[] { EPageLayer.BaseColor, EPageLayer.Normal };
                default: return new[] { EPageLayer.BaseColor, EPageLayer.Normal, EPageLayer.Mask };
            }
        }

        readonly int m_SlotTexels;
        readonly Layer[] m_Layers;
        readonly RenderTargetIdentifier[] m_ScratchTargets;

        public PhysicalLayers(VirtualTextureAsset asset, EPageLayer[] layers)
        {
            m_SlotTexels = asset.slotTexels;
            m_Layers = new Layer[layers.Length];
            m_ScratchTargets = new RenderTargetIdentifier[layers.Length];

            for (int i = 0; i < layers.Length; ++i)
            {
                GraphicsFormat format = IsColor(layers[i]) ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
                m_Layers[i] = new Layer
                {
                    kind = layers[i],
                    physical = CreateTexture($"VTPhysical{layers[i]}", asset.poolTexels, format),
                    scratch = CreateTexture($"VTScratch{layers[i]}", m_SlotTexels, format)
                };
                m_ScratchTargets[i] = m_Layers[i].scratch;
                Shader.SetGlobalTexture(s_GlobalIDs[(int)layers[i]], m_Layers[i].physical);
            }
        }

        /// <summary>绑定并清空 scratch，按层的顺序作为 MRT。</summary>
        public void BindScratch(CommandBuffer cmd)
        {
            cmd.SetRenderTarget(m_ScratchTargets, m_ScratchTargets[0]);
            cmd.ClearRenderTarget(false, true, Color.clear);
        }

        /// <summary>把 scratch 写进物理槽 slotCoord。</summary>
        public void Encode(CommandBuffer cmd, int2 slotCoord)
        {
            int2 origin = slotCoord * m_SlotTexels;
            foreach (Layer layer in m_Layers)
            {
                cmd.CopyTexture(layer.scratch, 0, 0, 0, 0, m_SlotTexels, m_SlotTexels, layer.physical, 0, 0, origin.x, origin.y);
            }
        }

        static RenderTexture CreateTexture(string name, int size, GraphicsFormat format)
        {
            var texture = new RenderTexture(size, size, format, GraphicsFormat.None, 1)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };
            texture.Create();
            return texture;
        }

        public void Dispose()
        {
            foreach (Layer layer in m_Layers)
            {
                Shader.SetGlobalTexture(s_GlobalIDs[(int)layer.kind], Texture2D.blackTexture);
                layer.physical.Release();
                layer.scratch.Release();
                Object.Destroy(layer.physical);
                Object.Destroy(layer.scratch);
            }
        }
    }
}
