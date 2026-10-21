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
    /// 物理页通路：每个启用层一张物理图，外加最多四组一页大小的 scratch。
    /// 物理图不生成 mip，着色器只采样 0 级；border 只服务于这一次双线性。
    /// 烘焙时生产者画进 scratch，再由 Encode 写进目标槽（需要时先经 PageEncoder 压缩）。
    /// </summary>
    internal class PhysicalLayers : IDisposable
    {
        class Layer
        {
            public EPageLayer kind;
            public Texture physical;
            public RenderTexture[] scratch;
            public RTHandle physicalHandle;
            public RTHandle[] scratchHandles;
            public TextureHandle physicalResource;
            public TextureHandle[] scratchResources;
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
        readonly RenderTargetIdentifier[][] m_ScratchTargets;
        readonly bool m_GroupLayers;
        public readonly int batchSize;
        readonly PageEncoder m_Encoder;

        /// <param name="encodeShader">压缩时使用的编码 compute；不压缩时为 null。</param>
        public PhysicalLayers(VirtualTextureAsset asset, EPageLayer[] layers, PageFormat format, ComputeShader encodeShader)
        {
            m_SlotTexels = asset.slotTexels;
            m_Layers = new Layer[layers.Length];
            batchSize = math.min(format.batchSize, asset.bakesPerRound);
            m_GroupLayers = format.batchSize > 1;
            m_ScratchTargets = new RenderTargetIdentifier[batchSize][];
            for (int page = 0; page < batchSize; ++page) { m_ScratchTargets[page] = new RenderTargetIdentifier[layers.Length]; }
            m_Encoder = format.isCompressed ? new PageEncoder(encodeShader, format, m_SlotTexels, layers, batchSize) : null;

            for (int i = 0; i < layers.Length; ++i)
            {
                GraphicsFormat scratchFormat = PageFormat.Of(EPageCompression.None).ForLayer(layers[i]).physicalFormat;
                m_Layers[i] = new Layer
                {
                    kind = layers[i],
                    physical = CreatePhysical($"VTPhysical{layers[i]}", asset.poolTexels, format.ForLayer(layers[i]).physicalFormat, format.isCompressed),
                    scratch = new RenderTexture[batchSize],
                    scratchHandles = new RTHandle[batchSize],
                    scratchResources = new TextureHandle[batchSize]
                };
                m_Layers[i].physicalHandle = RTHandles.Alloc(m_Layers[i].physical);
                for (int page = 0; page < batchSize; ++page)
                {
                    var scratch = CreateRenderTexture($"VTScratch{layers[i]}{page}", m_SlotTexels, scratchFormat);
                    m_Layers[i].scratch[page] = scratch;
                    m_Layers[i].scratchHandles[page] = RTHandles.Alloc(scratch);
                    m_ScratchTargets[page][i] = scratch;
                }
                Shader.SetGlobalTexture(s_GlobalIDs[(int)layers[i]], m_Layers[i].physical);
            }
        }

        public void ImportResources(RenderGraph graph, bool bake)
        {
            foreach (Layer layer in m_Layers)
            {
                layer.physicalResource = graph.ImportTexture(layer.physicalHandle);
                if (bake)
                {
                    for (int page = 0; page < batchSize; ++page) { layer.scratchResources[page] = graph.ImportTexture(layer.scratchHandles[page]); }
                }
            }
            if (bake) { m_Encoder?.ImportResources(graph); }
        }

        public void UseResources(IBaseRenderGraphBuilder builder, AccessFlags access)
        {
            foreach (Layer layer in m_Layers)
            {
                TextureHandle physical = layer.physicalResource;
                builder.UseTexture(physical, access);
                builder.SetGlobalTextureAfterPass(physical, s_GlobalIDs[(int)layer.kind]);
                if ((access & AccessFlags.Write) != 0)
                {
                    for (int page = 0; page < batchSize; ++page) { builder.UseTexture(layer.scratchResources[page], AccessFlags.ReadWrite); }
                }
            }
            if ((access & AccessFlags.Write) != 0) { m_Encoder?.UseResources(builder); }
        }

        /// <summary>绑定并清空 scratch，按层的顺序作为 MRT。</summary>
        public void BindScratch(CommandBuffer cmd, int page)
        {
            cmd.SetRenderTarget(m_ScratchTargets[page], m_ScratchTargets[page][0]);
            cmd.ClearRenderTarget(false, true, Color.clear);
        }

        /// <summary>同一工作集先编码再复制。大 ASTC 块逐层串行复用块图，避免实测吞吐回退。</summary>
        public void Encode(CommandBuffer cmd, ReadOnlySpan<int2> slots)
        {
            int count = slots.Length * m_Layers.Length;
            int stride = m_GroupLayers ? count : 1;
            for (int first = 0; first < count; first += stride)
            {
                using var encode = new ProfilingScope(cmd, m_Encoder != null ? PageEncoder.encodeSampler : null);
                for (int work = first; work < first + stride; ++work)
                {
                    int page = work / m_Layers.Length;
                    Layer layer = m_Layers[work % m_Layers.Length];
                    m_Encoder?.Dispatch(cmd, layer.scratch[page], layer.kind, page);
                }
                for (int work = first; work < first + stride; ++work)
                {
                    int page = work / m_Layers.Length;
                    Layer layer = m_Layers[work % m_Layers.Length];
                    int2 origin = slots[page] * m_SlotTexels;
                    if (m_Encoder != null) { m_Encoder.Copy(cmd, layer.kind, page, layer.physical, origin); }
                    else { cmd.CopyTexture(layer.scratch[page], 0, 0, 0, 0, m_SlotTexels, m_SlotTexels, layer.physical, 0, 0, origin.x, origin.y); }
                }
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
                Shader.SetGlobalTexture(s_GlobalIDs[(int)layer.kind], Texture2D.blackTexture);
                if (layer.physical is RenderTexture renderTexture) { renderTexture.Release(); }
                CoreUtils.Destroy(layer.physical);
                for (int page = 0; page < batchSize; ++page)
                {
                    layer.scratchHandles[page].Release();
                    layer.scratch[page].Release();
                    CoreUtils.Destroy(layer.scratch[page]);
                }
            }
        }
    }
}
