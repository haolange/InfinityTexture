using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;
using Object = UnityEngine.Object;

namespace Landscape.RuntimeVirtualTexture
{
    internal struct FPageTableInfo
    {
        public float4 pageData;
        public float4x4 matrix_M;
    }

    internal struct FPageDrawInfo : IComparable<FPageDrawInfo>
    {
        public int mip;
        public Rect rect;
        public float2 drawPos;

        public int CompareTo(FPageDrawInfo target)
        {
            return -(mip.CompareTo(target.mip));
        }
    }

    internal class FVirtualTextureRenderer : IDisposable
    {
        private int m_Limit;
        private int m_PageSize;
        private NativeList<FPageDrawInfo> m_DrawInfos;
        private NativeList<FPageRequestInfo> m_PageRequests;

        private Mesh m_DrawPageMesh;
        private Material m_DrawPageMaterial;

        private ComputeBuffer m_PageTableBuffer;
        private MaterialPropertyBlock m_PageTableProperty;

        public FVirtualTextureRenderer(in int pageSize, in int limit = 8)
        {
            this.m_Limit = limit;
            this.m_PageSize = pageSize;
            this.m_DrawInfos = new NativeList<FPageDrawInfo>(256, Allocator.Persistent);
            this.m_PageRequests = new NativeList<FPageRequestInfo>(256, Allocator.Persistent);
            this.m_PageTableBuffer = new ComputeBuffer(pageSize, Marshal.SizeOf(typeof(FPageTableInfo)));
            this.m_PageTableProperty = new MaterialPropertyBlock();
        }

        public void DrawPageTable(ScriptableRenderContext renderContext, CommandBuffer cmdBuffer, RenderTexture pageTableTexture, FPageProducer pageProducer)
        {
            m_DrawInfos.Clear();
            m_PageTableProperty.Clear();

            foreach (var pageCoord in pageProducer.activePageMap)
            {
                FPageTable pageTable = pageProducer.pageTables[pageCoord.Value.z];
                ref FPage page = ref pageTable.GetPage(pageCoord.Value.x, pageCoord.Value.y);
                if (page.payload.activeFrame != Time.frameCount) { continue; }

                var rectXY = new Vector2Int(page.rect.xMin, page.rect.yMin);
                while (rectXY.x < 0)
                {
                    rectXY.x += m_PageSize;
                }
                while (rectXY.y < 0)
                {
                    rectXY.y += m_PageSize;
                }

                FPageDrawInfo drawInfo;
                drawInfo.mip = page.mipLevel;
                drawInfo.rect = new Rect(rectXY.x, rectXY.y, page.rect.width, page.rect.height);
                drawInfo.drawPos = new float2((float)page.payload.tileIndex.x / 255, (float)page.payload.tileIndex.y / 255);
                m_DrawInfos.Add(drawInfo);
            }

            //Sort PageDrawInfo
            if (m_DrawInfos.Length == 0) { return; }
            FPageDrawInfoSortJob pageDrawInfoSortJob;
            pageDrawInfoSortJob.drawInfos = m_DrawInfos;
            pageDrawInfoSortJob.Run();

            //Build PageTableInfo
            NativeArray<FPageTableInfo> pageTableInfos = new NativeArray<FPageTableInfo>(m_DrawInfos.Length, Allocator.TempJob);
            FPageTableInfoBuildJob pageTableInfoBuildJob;
            pageTableInfoBuildJob.pageSize = m_PageSize;
            pageTableInfoBuildJob.drawInfos = m_DrawInfos;
            pageTableInfoBuildJob.pageTableInfos = pageTableInfos;
            pageTableInfoBuildJob.Schedule(m_DrawInfos.Length, 32).Complete();

            m_PageTableBuffer.SetData<FPageTableInfo>(pageTableInfos, 0, 0, pageTableInfos.Length);
            m_PageTableProperty.SetBuffer(VirtualTextureShaderID.PageTableBuffer, m_PageTableBuffer);

            cmdBuffer.SetRenderTarget(pageTableTexture);
            cmdBuffer.DrawMeshInstancedProcedural(m_DrawPageMesh, 0, m_DrawPageMaterial, 0, pageTableInfos.Length, m_PageTableProperty);
            renderContext.ExecuteCommandBuffer(cmdBuffer);
            cmdBuffer.Clear();

            pageTableInfos.Dispose();
        }

        public void Dispose()
        {
            m_DrawInfos.Dispose();
            m_PageRequests.Dispose();
            m_PageTableBuffer.Dispose();
            Object.DestroyImmediate(m_DrawPageMesh);
            Object.DestroyImmediate(m_DrawPageMaterial);
        }
    }
}
