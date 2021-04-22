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
        public FRect rect;
        public float2 drawPos;

        public int CompareTo(FPageDrawInfo target)
        {
            return -(mip.CompareTo(target.mip));
        }
    }

    internal class FPageRenderer : IDisposable
    {
        private int m_Limit;
        private int m_PageSize;
        private Mesh m_DrawPageMesh;
        private Material m_DrawPageMaterial;
        private ComputeBuffer m_PageTableBuffer;
        private MaterialPropertyBlock m_Property;
        private NativeList<FPageDrawInfo> m_DrawInfos;
        internal NativeList<FPageRequestInfo> pageRequests;

        public FPageRenderer(in int pageSize, in int limit = 12)
        {
            this.m_Limit = limit;
            this.m_PageSize = pageSize;
            this.m_Property = new MaterialPropertyBlock();
            this.m_DrawInfos = new NativeList<FPageDrawInfo>(256, Allocator.Persistent);
            this.pageRequests = new NativeList<FPageRequestInfo>(256, Allocator.Persistent);
            this.m_PageTableBuffer = new ComputeBuffer(pageSize / 2, Marshal.SizeOf(typeof(FPageTableInfo)), ComputeBufferType.Constant);
        }

        public void DrawPageTable(ScriptableRenderContext renderContext, CommandBuffer cmdBuffer, RenderTexture pageTableTexture, FPageProducer pageProducer)
        {
            m_Property.Clear();
            m_DrawInfos.Clear();

            //Build PageDrawInfo
            FPageDrawInfoBuildJob pageDrawInfoBuildJob;
            pageDrawInfoBuildJob.pageSize = m_PageSize;
            pageDrawInfoBuildJob.frameTime = Time.frameCount;
            pageDrawInfoBuildJob.drawInfos = m_DrawInfos;
            pageDrawInfoBuildJob.pageTables = pageProducer.pageTables;
            pageDrawInfoBuildJob.pageEnumerator = pageProducer.activePageMap.GetEnumerator();
            pageDrawInfoBuildJob.Run();

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
            pageTableInfoBuildJob.Schedule(m_DrawInfos.Length, 16).Complete();

            //Set PageTableBuffer
            m_PageTableBuffer.SetData<FPageTableInfo>(pageTableInfos, 0, 0, pageTableInfos.Length);
            m_Property.SetBuffer(VirtualTextureShaderID.PageTableBuffer, m_PageTableBuffer);

            //Draw PageTable
            /*cmdBuffer.SetRenderTarget(pageTableTexture);
            cmdBuffer.DrawMeshInstancedProcedural(m_DrawPageMesh, 0, m_DrawPageMaterial, 0, pageTableInfos.Length, m_Property);
            renderContext.ExecuteCommandBuffer(cmdBuffer);
            cmdBuffer.Clear();*/

            //Release
            pageTableInfos.Dispose();
        }

        public void DrawPageColor(ScriptableRenderContext renderContext, CommandBuffer cmdBuffer, RenderTargetIdentifier[] pageColorBuffers, FPageProducer pageProducer, ref FLruCache lruCache, in int tileNum, in int tileSize)
        {
            if (pageRequests.Length <= 0) { return; }
            FPageRequestInfoSortJob pageRequestInfoSortJob;
            pageRequestInfoSortJob.pageRequests = pageRequests;
            pageRequestInfoSortJob.Run();

            int count = m_Limit;
            while (count > 0 && pageRequests.Length > 0)
            {
                count--;
                FPageRequestInfo pageRequestInfo = pageRequests[pageRequests.Length - 1];
                pageRequests.RemoveAt(pageRequests.Length - 1);

                int3 pageUV = new int3(pageRequestInfo.pageX, pageRequestInfo.pageY, pageRequestInfo.mipLevel);
                FPageTable pageTable = pageProducer.pageTables[pageUV.z];
                ref FPage page = ref pageTable.GetPage(pageUV.x, pageUV.y);

                if (page.isNull == true || page.payload.pageRequestInfo.NotEquals(pageRequestInfo)) { return; }
                page.payload.pageRequestInfo.isNull = true;

                int2 pageCoord = new int2(lruCache.First % tileNum, lruCache.First / tileNum);
                if (lruCache.SetActive(pageCoord.y * tileNum + pageCoord.x))
                {
                    pageProducer.InvalidatePage(pageCoord);
                    //PageSystem.DrawMesh(new RectInt(pageCoord.x * tileSize, pageCoord.y * tileSize, tileSize, tileSize), pageRequestInfo);
                }

                page.payload.pageCoord = pageCoord;
                pageProducer.activePageMap.Add(pageCoord, pageUV);
                //Debug.Log(lruCache.First);
            }
        }

        public void Dispose()
        {
            m_DrawInfos.Dispose();
            pageRequests.Dispose();
            m_PageTableBuffer.Dispose();
            Object.DestroyImmediate(m_DrawPageMesh);
            Object.DestroyImmediate(m_DrawPageMaterial);
        }
    }
}
