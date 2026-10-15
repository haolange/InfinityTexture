using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>
    /// 物理图的存储方式。BC / ETC2 / ASTC 都是有损块压缩，每块 128 位。
    /// BC 用 BC3，ETC2 用 ETC2 RGBA8（EAC alpha），二者码率固定；颜色层用 sRGB 格式，数据层不做 sRGB。
    /// </summary>
    public enum EPageCompression
    {
        None,
        BC,
        ETC2
    }

    /// <summary>一种压缩在物理图上的格式与块尺寸。</summary>
    internal readonly struct PageFormat
    {
        public const int BlockBytes = 16;

        public readonly EPageCompression compression;
        /// <summary>块的像素宽高；None 时为 1。</summary>
        public readonly int2 blockSize;

        PageFormat(EPageCompression compression, int2 blockSize)
        {
            this.compression = compression;
            this.blockSize = blockSize;
        }

        public static PageFormat Of(EPageCompression compression)
        {
            return new PageFormat(compression, compression == EPageCompression.None ? new int2(1, 1) : new int2(4, 4));
        }

        public bool isCompressed => compression != EPageCompression.None;

        public GraphicsFormat PhysicalFormat(bool isColor)
        {
            switch (compression)
            {
                case EPageCompression.BC: return isColor ? GraphicsFormat.RGBA_DXT5_SRGB : GraphicsFormat.RGBA_DXT5_UNorm;
                case EPageCompression.ETC2: return isColor ? GraphicsFormat.RGBA_ETC2_SRGB : GraphicsFormat.RGBA_ETC2_UNorm;
                default: return isColor ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
            }
        }

        public string kernel => compression == EPageCompression.BC ? "EncodeBC3" : "EncodeETC2";

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

        readonly ComputeShader m_Shader;
        readonly int m_Kernel;
        readonly int2 m_BlockCount;
        readonly RenderTexture m_Blocks;

        public PageEncoder(ComputeShader shader, PageFormat format, int slotTexels)
        {
            m_Shader = shader;
            m_Kernel = shader.FindKernel(format.kernel);
            m_BlockCount = format.BlocksPerSlot(slotTexels);
            m_Blocks = new RenderTexture(m_BlockCount.x, m_BlockCount.y, GraphicsFormat.R32G32B32A32_UInt, GraphicsFormat.None, 1)
            {
                name = "VTEncodedBlocks",
                enableRandomWrite = true
            };
            m_Blocks.Create();
        }

        public void Encode(CommandBuffer cmd, RenderTexture scratch, bool isColor, Texture physical, int2 origin)
        {
            cmd.SetComputeTextureParam(m_Shader, m_Kernel, s_SourceID, scratch);
            cmd.SetComputeTextureParam(m_Shader, m_Kernel, s_BlocksID, m_Blocks);
            cmd.SetComputeIntParam(m_Shader, s_BlockCountID, m_BlockCount.x);
            cmd.SetComputeIntParam(m_Shader, s_SourceIsColorID, isColor ? 1 : 0);
            cmd.DispatchCompute(m_Shader, m_Kernel, (m_BlockCount.x + 7) / 8, (m_BlockCount.y + 7) / 8, 1);
            cmd.CopyTexture(m_Blocks, 0, 0, 0, 0, m_BlockCount.x, m_BlockCount.y, physical, 0, 0, origin.x, origin.y);
        }

        public void Dispose()
        {
            m_Blocks.Release();
            Object.Destroy(m_Blocks);
        }
    }
}
