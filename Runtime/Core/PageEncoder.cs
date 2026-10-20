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
    /// 物理图的存储方式。BC / ETC2 / ASTC 都是有损块压缩，每块 128 位。
    /// BC 用 BC3，ETC2 用 ETC2 RGBA8（EAC alpha），二者码率固定；ASTC 的码率由 EAstcBlock 决定。
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
        public const int BlockBytes = 16;

        public readonly EPageCompression compression;
        public readonly EAstcBlock astcBlock;
        /// <summary>块的像素宽高；None 时为 1。</summary>
        public readonly int2 blockSize;

        PageFormat(EPageCompression compression, EAstcBlock astcBlock, int2 blockSize)
        {
            this.compression = compression;
            this.astcBlock = astcBlock;
            this.blockSize = blockSize;
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

        public GraphicsFormat PhysicalFormat(bool isColor)
        {
            switch (compression)
            {
                case EPageCompression.BC: return isColor ? GraphicsFormat.RGBA_DXT5_SRGB : GraphicsFormat.RGBA_DXT5_UNorm;
                case EPageCompression.ETC2: return isColor ? GraphicsFormat.RGBA_ETC2_SRGB : GraphicsFormat.RGBA_ETC2_UNorm;
                case EPageCompression.ASTC: return AstcFormat(isColor);
                default: return isColor ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
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
                    case EPageCompression.BC: return "EncodeBC3";
                    case EPageCompression.ETC2: return "EncodeETC2";
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
            return isCompressed ? blocks.x * blocks.y * BlockBytes : slotTexels * slotTexels * 4;
        }
    }

    /// <summary>
    /// 压缩通路：在页烘焙之后把 scratch 编码成块，写进物理槽。
    /// 编码结果先写进 R32G32B32A32_UInt 的块图（一个 texel = 一块），再按块 CopyTexture 到压缩物理图。
    /// </summary>
    internal class PageEncoder : IDisposable
    {
        static readonly int s_SourceID = Shader.PropertyToID("_Source");
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
        readonly int m_Kernel;
        readonly int2 m_BlockCount;
        readonly int2 m_ThreadGroupSize;
        readonly RenderTexture m_Blocks;
        readonly RTHandle m_BlockHandle;
        TextureHandle m_BlockResource;
        readonly AstcEncoding m_AstcColor;
        readonly AstcEncoding m_AstcData;

        public PageEncoder(ComputeShader shader, PageFormat format, int slotTexels)
        {
            m_Shader = shader;
            m_Kernel = shader.FindKernel(format.kernel);
            shader.GetKernelThreadGroupSizes(m_Kernel, out uint threadsX, out uint threadsY, out _);
            m_ThreadGroupSize = new int2((int)threadsX, (int)threadsY);
            if (format.compression == EPageCompression.ASTC)
            {
                m_AstcColor = new AstcEncoding(AstcLayout.Of(format.astcBlock, true));
                m_AstcData = new AstcEncoding(AstcLayout.Of(format.astcBlock, false));
            }
            m_BlockCount = format.BlocksPerSlot(slotTexels);
            m_Blocks = new RenderTexture(m_BlockCount.x, m_BlockCount.y, GraphicsFormat.R32G32B32A32_UInt, GraphicsFormat.None, 1)
            {
                name = "VTEncodedBlocks",
                enableRandomWrite = true
            };
            m_Blocks.Create();
            m_BlockHandle = RTHandles.Alloc(m_Blocks);
        }

        public void ImportResources(RenderGraph graph) => m_BlockResource = graph.ImportTexture(m_BlockHandle);

        public void UseResources(IBaseRenderGraphBuilder builder)
        {
            builder.UseTexture(m_BlockResource, AccessFlags.ReadWrite);
        }

        public void Encode(CommandBuffer cmd, RenderTexture scratch, bool isColor, Texture physical, int2 origin)
        {
            cmd.SetComputeTextureParam(m_Shader, m_Kernel, s_SourceID, scratch);
            cmd.SetComputeTextureParam(m_Shader, m_Kernel, s_BlocksID, m_Blocks);
            cmd.SetComputeIntParam(m_Shader, s_BlockCountID, m_BlockCount.x);
            cmd.SetComputeIntParam(m_Shader, s_SourceIsColorID, isColor ? 1 : 0);
            AstcEncoding astc = isColor ? m_AstcColor : m_AstcData;
            if (astc != null)
            {
                cmd.SetComputeBufferParam(m_Shader, m_Kernel, s_AstcTablesID, astc.tables);
                cmd.SetComputeIntParams(m_Shader, s_AstcBlockConfigID, astc.blockConfig);
                cmd.SetComputeIntParams(m_Shader, s_AstcIseConfigID, astc.iseConfig);
                cmd.SetComputeIntParams(m_Shader, s_AstcPlaneConfigID, astc.planeConfig);
            }
            cmd.DispatchCompute(m_Shader, m_Kernel, (m_BlockCount.x + m_ThreadGroupSize.x - 1) / m_ThreadGroupSize.x, (m_BlockCount.y + m_ThreadGroupSize.y - 1) / m_ThreadGroupSize.y, 1);
            cmd.CopyTexture(m_Blocks, 0, 0, 0, 0, m_BlockCount.x, m_BlockCount.y, physical, 0, 0, origin.x, origin.y);
        }

        public void Dispose()
        {
            m_AstcColor?.Dispose();
            m_AstcData?.Dispose();
            m_BlockHandle.Release();
            m_Blocks.Release();
            Object.Destroy(m_Blocks);
        }
    }
}
