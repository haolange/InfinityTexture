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

    internal struct FDrawPageParameter
    {
        public FRect volumeRect;
        public Terrain[] terrainList;
    }

    internal class FPageRenderer : IDisposable
    {
        private int m_Limit;
        private int m_PageSize;
        private Mesh m_DrawPageMesh;
        private Mesh m_TriangleMesh;
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
            this.m_PageTableBuffer = new ComputeBuffer(pageSize / 2, Marshal.SizeOf(typeof(FPageTableInfo)));
            this.m_DrawPageMesh = FVirtualTextureUtility.BuildQuadMesh();
            this.m_TriangleMesh = FVirtualTextureUtility.BuildTriangleMesh();
            this.m_DrawPageMaterial = new Material(Shader.Find("VirtualTexture/DrawPageTable"));
            this.m_DrawColorMaterial = new Material(Shader.Find("VirtualTexture/DrawPageColor"));
            this.m_DrawPageMaterial.enableInstancing = true;
        }

        public void DrawPageTable(CommandBuffer cmdBuffer, FPageProducer pageProducer)
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

            //Get NativeData
            NativeArray<FPageTableInfo> pageTableInfos = new NativeArray<FPageTableInfo>(m_DrawInfos.Length, Allocator.TempJob);

            //Build PageTableInfo
            FPageTableInfoBuildJob pageTableInfoBuildJob;
            pageTableInfoBuildJob.length = m_DrawInfos.Length;
            pageTableInfoBuildJob.pageSize = m_PageSize;
            pageTableInfoBuildJob.drawInfos = m_DrawInfos;
            pageTableInfoBuildJob.pageTableInfos = pageTableInfos;
            pageTableInfoBuildJob.Run();

            //Draw PageTable
            m_Property.Clear();
            m_PageTableBuffer.SetData<FPageTableInfo>(pageTableInfos, 0, 0, pageTableInfos.Length);
            m_Property.SetBuffer("_PageTableBuffer", m_PageTableBuffer);
            cmdBuffer.DrawMeshInstancedProcedural(m_DrawPageMesh, 0, m_DrawPageMaterial, 1, pageTableInfos.Length, m_Property);

            //Release NativeData
            pageTableInfos.Dispose();
        }

        public void DrawPageColor(CommandBuffer cmdBuffer, FPageProducer pageProducer, VirtualTextureAsset virtualTexture, ref FLruCache lruCache, in FDrawPageParameter drawPageParameter)
        {
            if (pageRequests.Length <= 0) { return; }
            FPageRequestInfoSortJob pageRequestInfoSortJob;
            pageRequestInfoSortJob.pageRequests = pageRequests;
            pageRequestInfoSortJob.Run();

            int count = m_Limit;
            while (count > 0 && pageRequests.Length > 0)
            {
                count--;
                FPageRequestInfo requestInfo = pageRequests[pageRequests.Length - 1];
                pageRequests.RemoveAt(pageRequests.Length - 1);

                int3 pageUV = new int3(requestInfo.pageX, requestInfo.pageY, requestInfo.mipLevel);
                FPageTable pageTable = pageProducer.pageTables[pageUV.z];
                ref FPage page = ref pageTable.GetPage(pageUV.x, pageUV.y);

                if (page.isNull == true || page.payload.pageRequestInfo.NotEquals(requestInfo)) { continue; }
                page.payload.pageRequestInfo.isNull = true;

                int2 pageCoord = new int2(lruCache.First % virtualTexture.tileNum, lruCache.First / virtualTexture.tileNum);
                if (lruCache.SetActive(pageCoord.y * virtualTexture.tileNum + pageCoord.x))
                {
                    pageProducer.InvalidatePage(pageCoord);

                    FRectInt pageRect = new FRectInt(pageCoord.x * virtualTexture.TileSizePadding, pageCoord.y * virtualTexture.TileSizePadding, virtualTexture.TileSizePadding, virtualTexture.TileSizePadding);
                    RenderPage(cmdBuffer, virtualTexture, pageRect, requestInfo, drawPageParameter);
                }

                page.payload.pageCoord = pageCoord;
                pageProducer.activePageMap.Add(pageCoord, pageUV);
            }
        }

        private void RenderPage(CommandBuffer cmdBuffer, VirtualTextureAsset virtualTexture, in FRectInt pageRect, in FPageRequestInfo requestInfo, in FDrawPageParameter drawPageParameter)
        {
            int x = requestInfo.pageX;
            int y = requestInfo.pageY;
            int perSize = (int)Mathf.Pow(2, requestInfo.mipLevel);
            x = x - x % perSize;
            y = y - y % perSize;

            var pageSize = virtualTexture.pageSize;
            var paddingEffect = (int)virtualTexture.tileBorder * perSize * (drawPageParameter.volumeRect.width / pageSize) / virtualTexture.tileSize;
            var realRect = new Rect(drawPageParameter.volumeRect.xMin + (float)x / pageSize * drawPageParameter.volumeRect.width - paddingEffect,
                                    drawPageParameter.volumeRect.yMin + (float)y / pageSize * drawPageParameter.volumeRect.height - paddingEffect,
                                    drawPageParameter.volumeRect.width / pageSize * perSize + 2f * paddingEffect,
                                    drawPageParameter.volumeRect.width / pageSize * perSize + 2f * paddingEffect);


            var terRect = Rect.zero;
            foreach (var terrain in drawPageParameter.terrainList)
            {
                m_Property.Clear();

                terRect.xMin = terrain.transform.position.x;
                terRect.yMin = terrain.transform.position.z;
                terRect.width = terrain.terrainData.size.x;
                terRect.height = terrain.terrainData.size.z;

                if (!realRect.Overlaps(terRect)) { continue; }

                var needDrawRect = realRect;
                needDrawRect.xMin = Mathf.Max(realRect.xMin, terRect.xMin);
                needDrawRect.yMin = Mathf.Max(realRect.yMin, terRect.yMin);
                needDrawRect.xMax = Mathf.Min(realRect.xMax, terRect.xMax);
                needDrawRect.yMax = Mathf.Min(realRect.yMax, terRect.yMax);

                var scaleFactor = pageRect.width / realRect.width;

                var position = new FRect(pageRect.x + (needDrawRect.xMin - realRect.xMin) * scaleFactor,
                                        pageRect.y + (needDrawRect.yMin - realRect.yMin) * scaleFactor,
                                        needDrawRect.width * scaleFactor,
                                        needDrawRect.height * scaleFactor);

                float4 scaleOffset = new float4(needDrawRect.width / terRect.width,
                                              needDrawRect.height / terRect.height,
                                              (needDrawRect.xMin - terRect.xMin) / terRect.width,
                                              (needDrawRect.yMin - terRect.yMin) / terRect.height);
                // 构建变换矩阵
                float l = position.x * 2.0f / virtualTexture.TextureSize - 1;
                float r = (position.x + position.width) * 2.0f / virtualTexture.TextureSize - 1;
                float b = position.y * 2.0f / virtualTexture.TextureSize - 1;
                float t = (position.y + position.height) * 2.0f / virtualTexture.TextureSize - 1;
                Matrix4x4 Matrix_MVP = new Matrix4x4();
                Matrix_MVP.m00 = r - l;
                Matrix_MVP.m03 = l;
                Matrix_MVP.m11 = t - b;
                Matrix_MVP.m13 = b;
                Matrix_MVP.m23 = -1;
                Matrix_MVP.m33 = 1;

                // 绘制贴图
                m_Property.SetVector("_SplatTileOffset", scaleOffset);
                m_Property.SetMatrix(Shader.PropertyToID("_Matrix_MVP"), GL.GetGPUProjectionMatrix(Matrix_MVP, true));

                int layerIndex = 0;
                for (int i = 0; i < terrain.terrainData.alphamapTextures.Length; ++i)
                {
                    var splatMap = terrain.terrainData.alphamapTextures[i];
                    m_Property.SetTexture("_SplatTexture", splatMap);

                    int index = 1;
                    for (; layerIndex < terrain.terrainData.terrainLayers.Length && index <= 4; layerIndex++)
                    {
                        var layer = terrain.terrainData.terrainLayers[layerIndex];
                        float2 tileScale = new float2(terrain.terrainData.size.x / layer.tileSize.x, terrain.terrainData.size.z / layer.tileSize.y);
                        float4 tileOffset = new float4(tileScale.x * scaleOffset.x, tileScale.y * scaleOffset.y, scaleOffset.z * tileScale.x, scaleOffset.w * tileScale.y);
                        m_Property.SetVector("_SurfaceTileOffset", tileOffset);
                        m_Property.SetTexture($"_AlbedoTexture{index}", layer.diffuseTexture);
                        m_Property.SetTexture($"_NormalTexture{index}", layer.normalMapTexture);
                        index++;
                    }

                    cmdBuffer.DrawMesh(m_DrawPageMesh, Matrix4x4.identity, m_DrawColorMaterial, 0, layerIndex <= 4 ? 0 : 1, m_Property);
                }
            }
        }

        public void Dispose()
        {
            m_DrawInfos.Dispose();
            pageRequests.Dispose();
            m_PageTableBuffer.Dispose();
            Object.DestroyImmediate(m_DrawPageMesh);
            Object.DestroyImmediate(m_TriangleMesh);
            Object.DestroyImmediate(m_DrawPageMaterial);
            Object.DestroyImmediate(m_DrawColorMaterial);
        }
    }
}
