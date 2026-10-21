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
    /// 物理图的存储方式。BC：颜色 BC1、法线 BC5、Mask BC3；ETC2：颜色 RGB8、数据 RGBA8。
    /// ASTC 的码率由 EAstcBlock 决定。
    /// 颜色层用 sRGB 格式，数据层不做 sRGB。
    /// </summary>
    public enum EPageCompression
    {
        None,
        BC,
        ETC2,
        ASTC
    }

    /// <summary>一种压缩在物理图上的格式与块尺寸。</summary>
    internal readonly struct PageFormat
    {
        public readonly EPageLayer layer;
        public int blockBytes => isCompressed && layer == EPageLayer.BaseColor && compression != EPageCompression.ASTC ? 8 : 16;
        public GraphicsFormat blockFormat => blockBytes == 8 ? GraphicsFormat.R32G32_UInt : GraphicsFormat.R32G32B32A32_UInt;
        public PageFormat ForLayer(EPageLayer value) => new PageFormat(compression, astcBlock, blockSize, value);

        public readonly EPageCompression compression;
        public readonly EAstcBlock astcBlock;
        /// <summary>块的像素宽高；None 时为 1。</summary>
        public readonly int2 blockSize;

        PageFormat(EPageCompression compression, EAstcBlock astcBlock, int2 blockSize, EPageLayer layer = EPageLayer.BaseColor)
        {
            this.compression = compression;
            this.astcBlock = astcBlock;
            this.blockSize = blockSize;
            this.layer = layer;
        }

        /// <param name="astcBlock">只在 ASTC 时生效。</param>
        public static PageFormat Of(EPageCompression compression, EAstcBlock astcBlock = EAstcBlock.X4)
        {
            switch (compression)
            {
                case EPageCompression.None: return new PageFormat(compression, astcBlock, new int2(1, 1));
                case EPageCompression.ASTC: return new PageFormat(compression, astcBlock, new int2((int)astcBlock, (int)astcBlock));
                default: return new PageFormat(compression, astcBlock, new int2(4, 4));
            }
        }

        public bool isCompressed => compression != EPageCompression.None;

        public GraphicsFormat physicalFormat
        {
            get
            {
                bool isColor = layer == EPageLayer.BaseColor;
                switch (compression)
                {
                    case EPageCompression.BC: return isColor ? GraphicsFormat.RGBA_DXT1_SRGB : (layer == EPageLayer.Normal ? GraphicsFormat.RG_BC5_UNorm : GraphicsFormat.RGBA_DXT5_UNorm);
                    case EPageCompression.ETC2: return isColor ? GraphicsFormat.RGB_ETC2_SRGB : GraphicsFormat.RGBA_ETC2_UNorm;
                    case EPageCompression.ASTC: return AstcFormat(isColor);
                    default: return isColor ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
                }
            }
        }

        GraphicsFormat AstcFormat(bool isColor)
        {
            switch (astcBlock)
            {
                case EAstcBlock.X4: return isColor ? GraphicsFormat.RGBA_ASTC4X4_SRGB : GraphicsFormat.RGBA_ASTC4X4_UNorm;
                case EAstcBlock.X6: return isColor ? GraphicsFormat.RGBA_ASTC6X6_SRGB : GraphicsFormat.RGBA_ASTC6X6_UNorm;
                default: return isColor ? GraphicsFormat.RGBA_ASTC8X8_SRGB : GraphicsFormat.RGBA_ASTC8X8_UNorm;
            }
        }

        public string kernel
        {
            get
            {
                switch (compression)
                {
                    case EPageCompression.BC: return layer == EPageLayer.BaseColor ? "EncodeBC1" : (layer == EPageLayer.Normal ? "EncodeBC5" : "EncodeBC3");
                    case EPageCompression.ETC2: return layer == EPageLayer.BaseColor ? "EncodeETC2RGB" : "EncodeETC2";
                    default: return "EncodeASTC";
                }
            }
        }

        /// <summary>槽的像素边长必须是块宽高的整数倍，CopyTexture 才能按块对齐写入。</summary>
        public bool IsAligned(int slotTexels) => slotTexels % blockSize.x == 0 && slotTexels % blockSize.y == 0;

        public int2 BlocksPerSlot(int slotTexels) => slotTexels / blockSize;

        /// <summary>一个槽压缩后的字节数。</summary>
        public int SlotBytes(int slotTexels)
        {
            int2 blocks = BlocksPerSlot(slotTexels);
            return isCompressed ? blocks.x * blocks.y * blockBytes : slotTexels * slotTexels * 4;
        }
    }

    /// <summary>
    /// 压缩通路：在页烘焙之后把 scratch 编码成块，写进物理槽。
    /// 编码结果先写进 UInt 块图（一个 texel = 一块），再按块 CopyTexture 到压缩物理图。
    /// </summary>
    internal class PageEncoder : IDisposable
    {
        internal static readonly ProfilingSampler encodeSampler = new ProfilingSampler("VT Encode");
        static readonly int s_SourceID = Shader.PropertyToID("_Source");
        static readonly int s_Blocks64ID = Shader.PropertyToID("_Blocks64");
        static readonly int s_BlocksID = Shader.PropertyToID("_Blocks");
        static readonly int s_BlockCountID = Shader.PropertyToID("_BlockCount");
        static readonly int s_SourceIsColorID = Shader.PropertyToID("_SourceIsColor");
        static readonly int s_AstcTablesID = Shader.PropertyToID("_AstcTables");
        static readonly int s_AstcBlockConfigID = Shader.PropertyToID("_AstcBlockConfig");
        static readonly int s_AstcIseConfigID = Shader.PropertyToID("_AstcIseConfig");
        static readonly int s_AstcPlaneConfigID = Shader.PropertyToID("_AstcPlaneConfig");

        /// <summary>一套 ASTC 配置上传到着色器的参数与查找表（颜色层、数据层各一套）。</summary>
        class AstcEncoding : IDisposable
        {
            public readonly GraphicsBuffer tables;
            public readonly int[] blockConfig;
            public readonly int[] iseConfig;
            public readonly int[] planeConfig;

            public AstcEncoding(AstcLayout.Config config)
            {
                tables = new GraphicsBuffer(GraphicsBuffer.Target.Structured, AstcLayout.TableSize, sizeof(uint));
                tables.SetData(AstcLayout.BuildTables(config));
                blockConfig = new[] { config.block, config.gridWidth, config.gridHeight, config.blockMode };
                iseConfig = new[] { config.weightRange.kind, config.weightRange.bits, config.endpointRange.kind, config.endpointRange.bits };
                planeConfig = new[] { config.dualPlane ? 1 : 0, config.weightBits };
            }

            public void Dispose() => tables.Dispose();
        }

        readonly ComputeShader m_Shader;
        readonly int[] m_Kernels = new int[3];
        readonly PageFormat m_Format;
        readonly int2 m_BlockCount;
        readonly int2 m_ThreadGroupSize;
        readonly RenderTexture[] m_Blocks = new RenderTexture[2];
        readonly RTHandle[] m_BlockHandles = new RTHandle[2];
        readonly TextureHandle[] m_BlockResources = new TextureHandle[2];
        readonly AstcEncoding m_AstcColor;
        readonly AstcEncoding m_AstcData;

        public PageEncoder(ComputeShader shader, PageFormat format, int slotTexels, EPageLayer[] layers)
        {
            m_Shader = shader;
            m_Format = format;
            foreach (EPageLayer layer in layers) { m_Kernels[(int)layer] = shader.FindKernel(format.ForLayer(layer).kernel); }
            shader.GetKernelThreadGroupSizes(m_Kernels[(int)layers[0]], out uint threadsX, out uint threadsY, out _);
            m_ThreadGroupSize = new int2((int)threadsX, (int)threadsY);
            if (format.compression == EPageCompression.ASTC)
            {
                m_AstcColor = new AstcEncoding(AstcLayout.Of(format.astcBlock, true));
                m_AstcData = new AstcEncoding(AstcLayout.Of(format.astcBlock, false));
            }
            m_BlockCount = format.BlocksPerSlot(slotTexels);
            foreach (EPageLayer layer in layers)
            {
                PageFormat layerFormat = format.ForLayer(layer);
                int index = layerFormat.blockBytes == 8 ? 0 : 1;
                if (m_Blocks[index] != null) { continue; }
                var blocks = new RenderTexture(m_BlockCount.x, m_BlockCount.y, layerFormat.blockFormat, GraphicsFormat.None, 1)
                {
                    name = "VTEncodedBlocks" + layerFormat.blockBytes,
                    enableRandomWrite = true
                };
                blocks.Create();
                m_Blocks[index] = blocks;
                m_BlockHandles[index] = RTHandles.Alloc(blocks);
            }
        }

        public void ImportResources(RenderGraph graph)
        {
            for (int i = 0; i < m_BlockHandles.Length; ++i)
            {
                if (m_BlockHandles[i] != null) { m_BlockResources[i] = graph.ImportTexture(m_BlockHandles[i]); }
            }
        }

        public void UseResources(IBaseRenderGraphBuilder builder)
        {
            for (int i = 0; i < m_BlockHandles.Length; ++i)
            {
                if (m_BlockHandles[i] != null) { builder.UseTexture(m_BlockResources[i], AccessFlags.ReadWrite); }
            }
        }

        public void Encode(CommandBuffer cmd, RenderTexture scratch, EPageLayer layer, Texture physical, int2 origin)
        {
            using var encode = new ProfilingScope(cmd, encodeSampler);
            bool isColor = layer == EPageLayer.BaseColor;
            int kernel = m_Kernels[(int)layer];
            bool narrow = m_Format.ForLayer(layer).blockBytes == 8;
            RenderTexture blocks = m_Blocks[narrow ? 0 : 1];
            cmd.SetComputeTextureParam(m_Shader, kernel, s_SourceID, scratch);
            cmd.SetComputeTextureParam(m_Shader, kernel, narrow ? s_Blocks64ID : s_BlocksID, blocks);
            cmd.SetComputeIntParam(m_Shader, s_BlockCountID, m_BlockCount.x);
            cmd.SetComputeIntParam(m_Shader, s_SourceIsColorID, isColor ? 1 : 0);
            AstcEncoding astc = isColor ? m_AstcColor : m_AstcData;
            if (astc != null)
            {
                cmd.SetComputeBufferParam(m_Shader, kernel, s_AstcTablesID, astc.tables);
                cmd.SetComputeIntParams(m_Shader, s_AstcBlockConfigID, astc.blockConfig);
                cmd.SetComputeIntParams(m_Shader, s_AstcIseConfigID, astc.iseConfig);
                cmd.SetComputeIntParams(m_Shader, s_AstcPlaneConfigID, astc.planeConfig);
            }
            cmd.DispatchCompute(m_Shader, kernel, (m_BlockCount.x + m_ThreadGroupSize.x - 1) / m_ThreadGroupSize.x, (m_BlockCount.y + m_ThreadGroupSize.y - 1) / m_ThreadGroupSize.y, 1);
            cmd.CopyTexture(blocks, 0, 0, 0, 0, m_BlockCount.x, m_BlockCount.y, physical, 0, 0, origin.x, origin.y);
        }

        public void Dispose()
        {
            m_AstcColor?.Dispose();
            m_AstcData?.Dispose();
            for (int i = 0; i < m_Blocks.Length; ++i)
            {
                m_BlockHandles[i]?.Release();
                if (m_Blocks[i] == null) { continue; }
                m_Blocks[i].Release();
                CoreUtils.Destroy(m_Blocks[i]);
            }
        }
    }
}
