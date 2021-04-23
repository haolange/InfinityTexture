using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using System.Runtime.InteropServices;

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
        private Material m_DrawColorMaterial;
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

            this.m_DrawPageMesh = FVirtualTextureUtility.BuildQuadMesh();
            this.m_DrawPageMaterial = new Material(Shader.Find("VirtualTexture/DrawPageTable"));
            this.m_DrawColorMaterial = new Material(Shader.Find("VirtualTexture/DrawPageColor"));
            this.m_DrawPageMaterial.enableInstancing = true;
        }

        public void DrawPageTable(RenderTexture pageTableTexture, FPageProducer pageProducer)
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

            /*var pageEnumerator = pageProducer.activePageMap.GetEnumerator();
            while (pageEnumerator.MoveNext())
            {
                var pageCoord = pageEnumerator.Current.Value;
                FPageTable pageTable = pageProducer.pageTables[pageCoord.z];
                ref FPage page = ref pageTable.GetPage(pageCoord.x, pageCoord.y);
                if (page.payload.activeFrame != Time.frameCount) { continue; }

                int2 rectXY = new int2(page.rect.xMin, page.rect.yMin);
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
                drawInfo.rect = new FRect(rectXY.x, rectXY.y, page.rect.width, page.rect.height);
                drawInfo.drawPos = new float2((float)page.payload.pageCoord.x / 255, (float)page.payload.pageCoord.y / 255);
                m_DrawInfos.Add(drawInfo);
            }*/

            //Sort PageDrawInfo
            if (m_DrawInfos.Length == 0) { return; }
            FPageDrawInfoSortJob pageDrawInfoSortJob;
            pageDrawInfoSortJob.drawInfos = m_DrawInfos;
            pageDrawInfoSortJob.Run();
            //m_DrawInfos.Sort();

            //Build PageTableInfo
            NativeArray<Vector4> PageInfos = new NativeArray<Vector4>(m_DrawInfos.Length, Allocator.TempJob);
            NativeArray<Matrix4x4> Materix_MVP = new NativeArray<Matrix4x4>(m_DrawInfos.Length, Allocator.TempJob);
            for (int i = 0; i < m_DrawInfos.Length; ++i)
            {
                float size = m_DrawInfos[i].rect.width / m_PageSize;
                PageInfos[i] = new Vector4(m_DrawInfos[i].drawPos.x, m_DrawInfos[i].drawPos.y, m_DrawInfos[i].mip / 255f, 0);
                Materix_MVP[i] = Matrix4x4.TRS(new Vector3(m_DrawInfos[i].rect.x / m_PageSize, m_DrawInfos[i].rect.y / m_PageSize), Quaternion.identity, new Vector3(size, size, size));
            }

            //Set PageTableBuffer
            m_Property.Clear();
            m_Property.SetVectorArray("_PageInfo", PageInfos.ToArray());
            m_Property.SetMatrixArray("_Matrix_MVP", Materix_MVP.ToArray());

            //Draw PageTable
            CommandBuffer CmdBuffer = CommandBufferPool.Get("DrawPageTable");
            CmdBuffer.SetRenderTarget(pageTableTexture);
            CmdBuffer.DrawMeshInstanced(m_DrawPageMesh, 0, m_DrawPageMaterial, 0, Materix_MVP.ToArray(), Materix_MVP.Length, m_Property);
            Graphics.ExecuteCommandBuffer(CmdBuffer);

            //Release
            PageInfos.Dispose();
            Materix_MVP.Dispose();
        }

        public void DrawPageColor(RuntimeVirtualTextureSystem PageSystem, FPageProducer pageProducer, ref FLruCache lruCache, in int tileNum, in int tileSize)
        {
            if (pageRequests.Length <= 0) { return; }
            FPageRequestInfoSortJob pageRequestInfoSortJob;
            pageRequestInfoSortJob.pageRequests = pageRequests;
            pageRequestInfoSortJob.Run();
            //pageRequests.Sort();

            CommandBuffer cmdBuffer = CommandBufferPool.Get("DrawPageColor");
            cmdBuffer.Clear();
            cmdBuffer.SetRenderTarget(PageSystem.virtualTextureAsset.colorBuffers, PageSystem.virtualTextureAsset.colorBuffers[0]);

            int count = m_Limit;
            while (count > 0 && pageRequests.Length > 0)
            {
                count--;
                FPageRequestInfo PageRequestInfo = pageRequests[pageRequests.Length - 1];
                pageRequests.RemoveAt(pageRequests.Length - 1);

                int3 pageUV = new int3(PageRequestInfo.pageX, PageRequestInfo.pageY, PageRequestInfo.mipLevel);
                FPageTable pageTable = pageProducer.pageTables[pageUV.z];
                ref FPage page = ref pageTable.GetPage(pageUV.x, pageUV.y);

                if (page.isNull == true || page.payload.pageRequestInfo.NotEquals(PageRequestInfo)) { continue; }
                page.payload.pageRequestInfo.isNull = true;

                int2 pageCoord = new int2(lruCache.First % tileNum, lruCache.First / tileNum);
                if (lruCache.SetActive(pageCoord.y * tileNum + pageCoord.x))
                {
                    pageProducer.InvalidatePage(pageCoord);
                    PageSystem.DrawMesh(cmdBuffer, m_DrawPageMesh, m_DrawColorMaterial, m_Property, new FRectInt(pageCoord.x * tileSize, pageCoord.y * tileSize, tileSize, tileSize), PageRequestInfo);
                }

                page.payload.pageCoord = pageCoord;
                pageProducer.activePageMap.Add(pageCoord, pageUV);
            }

            Graphics.ExecuteCommandBuffer(cmdBuffer);
            CommandBufferPool.Release(cmdBuffer);
        }

        public void Dispose()
        {
            m_DrawInfos.Dispose();
            pageRequests.Dispose();
            m_PageTableBuffer.Dispose();
            Object.DestroyImmediate(m_DrawPageMesh);
            Object.DestroyImmediate(m_DrawPageMaterial);
            Object.DestroyImmediate(m_DrawColorMaterial);
        }
    }
}
