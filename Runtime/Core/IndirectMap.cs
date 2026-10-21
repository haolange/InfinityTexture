using System;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Unity.Collections.LowLevel.Unsafe;
using Object = UnityEngine.Object;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>往间接表某一级写一个矩形：rect 为该级上的格子范围，value 为 RGBA8 打包的 texel。</summary>
    internal struct IndirectWrite
    {
        public int4 rect;
        public uint value;
    }

    internal struct ResidentPage
    {
        public PageId page;
        public int slot;
    }

    /// <summary>
    /// 从物理槽的反向映射生成间接表的全部写入。
    /// 第 level 级写入所有 mip ≥ level 的驻留页，粗页在前、细页在后覆盖，
    /// 所以每个格子最终写的是"自身或最近的驻留祖先"，着色器不需要做 mip 循环。
    /// </summary>
    [BurstCompile]
    internal unsafe struct IndirectBuildJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public PhysicalPool* pool;
        public int mipCount;

        [ReadOnly] public NativeReference<bool> residencyChanged;
        public NativeList<ResidentPage> resident;
        public NativeList<IndirectWrite> writes;
        /// <summary>每级在 writes 里的 (起点, 个数)。</summary>
        public NativeArray<int2> levelRanges;

        public void Execute()
        {
            if (!residencyChanged.Value) { return; }

            resident.Clear();
            for (int slot = 0; slot < pool->capacity; ++slot)
            {
                PageId owner = (*pool)[slot].owner;
                if (owner.isValid) { resident.Add(new ResidentPage { page = owner, slot = slot }); }
            }
            resident.Sort(new CoarseFirst());

            writes.Clear();
            for (int level = 0; level < mipCount; ++level)
            {
                int start = writes.Length;
                for (int i = 0; i < resident.Length && resident[i].page.mip >= level; ++i)
                {
                    PageId page = resident[i].page;
                    writes.Add(new IndirectWrite
                    {
                        rect = VirtualTextureMath.Footprint(page, level),
                        value = VirtualTextureMath.IndirectValue(pool->SlotCoord(resident[i].slot), page.mip)
                    });
                }
                levelRanges[level] = new int2(start, writes.Length - start);
            }
        }

        struct CoarseFirst : System.Collections.Generic.IComparer<ResidentPage>
        {
            public int Compare(ResidentPage a, ResidentPage b)
            {
                if (a.page.mip != b.page.mip) { return b.page.mip.CompareTo(a.page.mip); }
                return a.page.packed.CompareTo(b.page.packed);
            }
        }
    }

    /// <summary>
    /// GPU 间接表：pageCount x pageCount、带完整 mip 链的 RGBA8 纹理，第 k 级只描述虚拟 mip k。
    /// 着色器在第 k 级点采样一次，得到该处应该读的物理槽和页所在 mip。
    /// </summary>
    internal class IndirectMap : IDisposable
    {
        static readonly int s_IndirectMapID = Shader.PropertyToID("_VTIndirectMap");
        static readonly int s_WritesID = Shader.PropertyToID("_IndirectWrites");
        static readonly int s_WriteBaseID = Shader.PropertyToID("_IndirectWriteBase");
        static readonly int s_LevelSizeID = Shader.PropertyToID("_IndirectLevelSize");

        public readonly int mipCount;
        public RTHandle texture { get; }

        readonly RenderTexture m_Texture;
        readonly GraphicsBuffer m_Writes;
        readonly Material m_Material;
        readonly MaterialPropertyBlock m_Properties = new MaterialPropertyBlock();

        public IndirectMap(int pageCount, int poolCapacity, Shader shader)
        {
            mipCount = math.tzcnt(pageCount) + 1;
            m_Texture = new RenderTexture(new RenderTextureDescriptor(pageCount, pageCount, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None, mipCount)
            {
                useMipMap = true,
                autoGenerateMips = false
            })
            {
                name = "VTIndirectMap",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            m_Texture.Create();
            texture = RTHandles.Alloc(m_Texture);

            m_Writes = new GraphicsBuffer(GraphicsBuffer.Target.Structured, poolCapacity * mipCount, UnsafeUtility.SizeOf<IndirectWrite>());
            m_Material = new Material(shader);
            Shader.SetGlobalTexture(s_IndirectMapID, m_Texture);
        }

        /// <summary>逐级清空后，按 levelRanges 画出每一级的写入。</summary>
        public void Update(CommandBuffer cmd, NativeList<IndirectWrite> writes, NativeArray<int2> levelRanges)
        {
            if (writes.Length > 0) { cmd.SetBufferData(m_Writes, writes.AsArray(), 0, 0, writes.Length); }

            for (int level = 0; level < mipCount; ++level)
            {
                cmd.SetRenderTarget(m_Texture, level);
                cmd.ClearRenderTarget(false, true, Color.clear);

                int2 range = levelRanges[level];
                if (range.y == 0) { continue; }

                m_Properties.SetBuffer(s_WritesID, m_Writes);
                m_Properties.SetInt(s_WriteBaseID, range.x);
                m_Properties.SetFloat(s_LevelSizeID, m_Texture.width >> level);
                cmd.DrawProcedural(Matrix4x4.identity, m_Material, 0, MeshTopology.Triangles, 6, range.y, m_Properties);
            }
        }

        public void Dispose()
        {
            Shader.SetGlobalTexture(s_IndirectMapID, Texture2D.blackTexture);
            texture.Release();
            m_Texture.Release();
            CoreUtils.Destroy(m_Texture);
            m_Writes.Dispose();
            CoreUtils.Destroy(m_Material);
        }
    }
}
