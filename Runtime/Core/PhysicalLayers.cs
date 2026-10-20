using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
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
    /// 烘焙时生产者画进 scratch，再由 Encode 写进目标槽（需要时先经 PageEncoder 压缩）。
    /// </summary>
    internal class PhysicalLayers : IDisposable
    {
        class Layer
        {
            public EPageLayer kind;
            public Texture physical;
            public RenderTexture scratch;
            public RTHandle physicalHandle;
            public RTHandle scratchHandle;
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
        readonly PageEncoder m_Encoder;

        /// <param name="encodeShader">压缩时使用的编码 compute；不压缩时为 null。</param>
        public PhysicalLayers(VirtualTextureAsset asset, EPageLayer[] layers, PageFormat format, ComputeShader encodeShader)
        {
            m_SlotTexels = asset.slotTexels;
            m_Layers = new Layer[layers.Length];
            m_ScratchTargets = new RenderTargetIdentifier[layers.Length];
            m_Encoder = format.isCompressed ? new PageEncoder(encodeShader, format, m_SlotTexels) : null;

            for (int i = 0; i < layers.Length; ++i)
            {
                bool isColor = IsColor(layers[i]);
                GraphicsFormat scratchFormat = PageFormat.Of(EPageCompression.None).PhysicalFormat(isColor);
                m_Layers[i] = new Layer
                {
                    kind = layers[i],
                    physical = CreatePhysical($"VTPhysical{layers[i]}", asset.poolTexels, format.PhysicalFormat(isColor), format.isCompressed),
                    scratch = CreateRenderTexture($"VTScratch{layers[i]}", m_SlotTexels, scratchFormat)
                };
                m_Layers[i].physicalHandle = RTHandles.Alloc(m_Layers[i].physical);
                m_Layers[i].scratchHandle = RTHandles.Alloc(m_Layers[i].scratch);
                m_ScratchTargets[i] = m_Layers[i].scratch;
                Shader.SetGlobalTexture(s_GlobalIDs[(int)layers[i]], m_Layers[i].physical);
            }
        }

        public void UseResources(RenderGraph graph, IBaseRenderGraphBuilder builder, AccessFlags access)
        {
            foreach (Layer layer in m_Layers)
            {
                TextureHandle physical = graph.ImportTexture(layer.physicalHandle);
                builder.UseTexture(physical, access);
                builder.SetGlobalTextureAfterPass(physical, s_GlobalIDs[(int)layer.kind]);
                if ((access & AccessFlags.Write) != 0)
                {
                    builder.UseTexture(graph.ImportTexture(layer.scratchHandle), AccessFlags.ReadWrite);
                }
            }
            if ((access & AccessFlags.Write) != 0) { m_Encoder?.UseResources(graph, builder); }
        }

        /// <summary>绑定并清空 scratch，按层的顺序作为 MRT。</summary>
        public void BindScratch(CommandBuffer cmd)
        {
            cmd.SetRenderTarget(m_ScratchTargets, m_ScratchTargets[0]);
            cmd.ClearRenderTarget(false, true, Color.clear);
        }

        /// <summary>把 scratch 写进物理槽 slotCoord：不压缩时直接拷贝，否则先编码成块。</summary>
        public void Encode(CommandBuffer cmd, int2 slotCoord)
        {
            int2 origin = slotCoord * m_SlotTexels;
            foreach (Layer layer in m_Layers)
            {
                if (m_Encoder != null) { m_Encoder.Encode(cmd, layer.scratch, IsColor(layer.kind), layer.physical, origin); }
                else { cmd.CopyTexture(layer.scratch, 0, 0, 0, 0, m_SlotTexels, m_SlotTexels, layer.physical, 0, 0, origin.x, origin.y); }
            }
        }

        /// <summary>物理图：不生成 mip、双线性、clamp、aniso 0。压缩格式用 Texture2D 接收块拷贝。</summary>
        static Texture CreatePhysical(string name, int size, GraphicsFormat format, bool compressed)
        {
            if (!compressed) { return CreateRenderTexture(name, size, format); }

            var texture = new Texture2D(size, size, format, 1, TextureCreationFlags.DontInitializePixels)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };
            texture.Apply(false, true); // 只保留 GPU 副本，内容全部由块拷贝写入
            return texture;
        }

        static RenderTexture CreateRenderTexture(string name, int size, GraphicsFormat format)
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
            m_Encoder?.Dispose();
            foreach (Layer layer in m_Layers)
            {
                layer.physicalHandle.Release();
                layer.scratchHandle.Release();
                Shader.SetGlobalTexture(s_GlobalIDs[(int)layer.kind], Texture2D.blackTexture);
                if (layer.physical is RenderTexture renderTexture) { renderTexture.Release(); }
                layer.scratch.Release();
                Object.Destroy(layer.physical);
                Object.Destroy(layer.scratch);
            }
        }
    }
}
