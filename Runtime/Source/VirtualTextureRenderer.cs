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

    internal static class FPageShaderID
    {
        public static int PageTableBuffer = Shader.PropertyToID("_PageTableBuffer");
        public static int SplatTileOffset = Shader.PropertyToID("_SplatTileOffset");
        public static int SplatTexture = Shader.PropertyToID("_SplatTexture");
        public static int SurfaceTileOffset = Shader.PropertyToID("_SurfaceTileOffset");
        public static int[] AlbedoTexture = new int[4] { Shader.PropertyToID("_AlbedoTexture1"), Shader.PropertyToID("_AlbedoTexture2"), Shader.PropertyToID("_AlbedoTexture3"), Shader.PropertyToID("_AlbedoTexture4") };
        public static int[] NormalTexture = new int[4] { Shader.PropertyToID("_NormalTexture1"), Shader.PropertyToID("_NormalTexture2"), Shader.PropertyToID("_NormalTexture3"), Shader.PropertyToID("_NormalTexture4") };
        public static int Size = Shader.PropertyToID("_Size");
        public static int SrcTexture = Shader.PropertyToID("_SrcTexture");
        public static int DscTexture = Shader.PropertyToID("_DscTexture");   
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
        internal NativeList<FPageLoadInfo> loadRequests;

        public FPageRenderer(in int pageSize, in int limit = 16)
        {
            this.m_Limit = limit;
            this.m_PageSize = pageSize;
            this.m_Property = new MaterialPropertyBlock();
            this.m_DrawInfos = new NativeList<FPageDrawInfo>(256, Allocator.Persistent);
            this.loadRequests = new NativeList<FPageLoadInfo>(4096 * 2, Allocator.Persistent);
            this.m_PageTableBuffer = new ComputeBuffer(pageSize, Marshal.SizeOf(typeof(FPageTableInfo)));
            this.m_DrawPageMesh = FVirtualTextureUtility.BuildQuadMesh();
            this.m_TriangleMesh = FVirtualTextureUtility.BuildTriangleMesh();
            this.m_DrawPageMaterial = new Material(Shader.Find("VirtualTexture/DrawPageTable"));
            this.m_DrawColorMaterial = new Material(Shader.Find("VirtualTexture/DrawPageColor"));
            this.m_DrawPageMaterial.enableInstancing = true;
        }

        public void DrawPageTable(ScriptableRenderContext renderContext, CommandBuffer cmdBuffer, FPageProducer pageProducer)
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
            pageTableInfoBuildJob.pageSize = m_PageSize;
            pageTableInfoBuildJob.drawInfos = m_DrawInfos;
            pageTableInfoBuildJob.pageTableInfos = pageTableInfos;
            pageTableInfoBuildJob.Run(m_DrawInfos.Length);

            //Draw PageTable
            m_Property.Clear();
            m_PageTableBuffer.SetData<FPageTableInfo>(pageTableInfos, 0, 0, pageTableInfos.Length);
            m_Property.SetBuffer(FPageShaderID.PageTableBuffer, m_PageTableBuffer);
            cmdBuffer.DrawMeshInstancedProcedural(m_DrawPageMesh, 0, m_DrawPageMaterial, 1, pageTableInfos.Length, m_Property);

            //Release NativeData
            pageTableInfos.Dispose();
        }

        public void DrawPageColor(ScriptableRenderContext renderContext, CommandBuffer cmdBuffer, FPageProducer pageProducer, VirtualTextureAsset virtualTexture, ref FLruCache lruCache, in FDrawPageParameter drawPageParameter)
        {
            if (loadRequests.Length <= 0) { return; }
            FPageRequestInfoSortJob pageRequestInfoSortJob;
            pageRequestInfoSortJob.loadRequests = loadRequests;
            pageRequestInfoSortJob.Run();

            int count = m_Limit;
            while (count > 0 && loadRequests.Length > 0)
            {
                count--;
                FPageLoadInfo loadRequest = loadRequests[loadRequests.Length - 1];
                loadRequests.RemoveAt(loadRequests.Length - 1);

                int3 pageUV = new int3(loadRequest.pageX, loadRequest.pageY, loadRequest.mipLevel);
                FPageTable pageTable = pageProducer.pageTables[pageUV.z];
                ref FPage page = ref pageTable.GetPage(pageUV.x, pageUV.y);

                if (page.isNull == true) { continue; }
                page.payload.notLoading = true;

                int2 pageCoord = new int2(lruCache.First % virtualTexture.tileNum, lruCache.First / virtualTexture.tileNum);
                if (lruCache.SetActive(pageCoord.y * virtualTexture.tileNum + pageCoord.x))
                {
                    pageProducer.InvalidatePage(pageCoord);

                    FRectInt pageRect = new FRectInt(pageCoord.x * virtualTexture.TileSizePadding, pageCoord.y * virtualTexture.TileSizePadding, virtualTexture.TileSizePadding, virtualTexture.TileSizePadding);
                    RenderPage(cmdBuffer, virtualTexture, pageRect, loadRequest, drawPageParameter);
                }

                page.payload.pageCoord = pageCoord;
                pageProducer.activePageMap.Add(pageCoord, pageUV);
            }
        }

        private void RenderPage(CommandBuffer cmdBuffer, VirtualTextureAsset virtualTexture, in FRectInt pageRect, in FPageLoadInfo loadRequest, in FDrawPageParameter drawPageParameter)
        {
            int x = loadRequest.pageX;
            int y = loadRequest.pageY;
            int perSize = (int)Mathf.Pow(2, loadRequest.mipLevel);
            x = x - x % perSize;
            y = y - y % perSize;

            var padding = (int)virtualTexture.tileBorder * perSize * (drawPageParameter.volumeRect.width / virtualTexture.pageSize) / virtualTexture.tileSize;
            var volumeRect = new Rect(drawPageParameter.volumeRect.xMin + (float)x / virtualTexture.pageSize * drawPageParameter.volumeRect.width - padding, drawPageParameter.volumeRect.yMin + (float)y / virtualTexture.pageSize * drawPageParameter.volumeRect.height - padding, drawPageParameter.volumeRect.width / virtualTexture.pageSize * perSize + 2f * padding, drawPageParameter.volumeRect.width / virtualTexture.pageSize * perSize + 2f * padding);
            
            foreach (var terrain in drawPageParameter.terrainList)
            {
                var terrainRect = Rect.zero;
                terrainRect.xMin = terrain.transform.position.x;
                terrainRect.yMin = terrain.transform.position.z;
                terrainRect.width = terrain.terrainData.size.x;
                terrainRect.height = terrain.terrainData.size.z;

                if (!volumeRect.Overlaps(terrainRect)) { continue; }

                var maxRect = volumeRect;
                maxRect.xMin = Mathf.Max(volumeRect.xMin, terrainRect.xMin);
                maxRect.yMin = Mathf.Max(volumeRect.yMin, terrainRect.yMin);
                maxRect.xMax = Mathf.Min(volumeRect.xMax, terrainRect.xMax);
                maxRect.yMax = Mathf.Min(volumeRect.yMax, terrainRect.yMax);

                var scaleFactor = pageRect.width / volumeRect.width;
                FRect offsetRect = new FRect(pageRect.x + (maxRect.xMin - volumeRect.xMin) * scaleFactor, pageRect.y + (maxRect.yMin - volumeRect.yMin) * scaleFactor, maxRect.width * scaleFactor, maxRect.height * scaleFactor);
                float l = offsetRect.x * 2.0f / virtualTexture.TextureSize - 1;
                float r = (offsetRect.x + offsetRect.width) * 2.0f / virtualTexture.TextureSize - 1;
                float b = offsetRect.y * 2.0f / virtualTexture.TextureSize - 1;
                float t = (offsetRect.y + offsetRect.height) * 2.0f / virtualTexture.TextureSize - 1;
                Matrix4x4 Matrix_MVP = new Matrix4x4();
                Matrix_MVP.m00 = r - l;
                Matrix_MVP.m03 = l;
                Matrix_MVP.m11 = t - b;
                Matrix_MVP.m13 = b;
                Matrix_MVP.m23 = -1;
                Matrix_MVP.m33 = 1;

                float4 scaleOffset = new float4(maxRect.width / terrainRect.width, maxRect.height / terrainRect.height, (maxRect.xMin - terrainRect.xMin) / terrainRect.width, (maxRect.yMin - terrainRect.yMin) / terrainRect.height);
                m_Property.Clear();
                m_Property.SetVector(FPageShaderID.SplatTileOffset, scaleOffset);
                m_Property.SetMatrix(Shader.PropertyToID("_Matrix_MVP"), GL.GetGPUProjectionMatrix(Matrix_MVP, true));

                int layerIndex = 0;
                var terrainLayers = terrain.terrainData.terrainLayers;

                for (int i = 0; i < terrain.terrainData.alphamapTextureCount; ++i)
                {
                    m_Property.SetTexture(FPageShaderID.SplatTexture, terrain.terrainData.GetAlphamapTexture(i));

                    int index = 1;
                    for (; layerIndex < terrain.terrainData.alphamapLayers && index <= 4; ++layerIndex)
                    {
                        var terrainLayer = terrainLayers[layerIndex];
                        float2 tileScale = new float2(terrain.terrainData.size.x / terrainLayer.tileSize.x, terrain.terrainData.size.z / terrainLayer.tileSize.y);
                        float4 tileOffset = new float4(tileScale.x * scaleOffset.x, tileScale.y * scaleOffset.y, scaleOffset.z * tileScale.x, scaleOffset.w * tileScale.y);
                        m_Property.SetVector(FPageShaderID.SurfaceTileOffset, tileOffset);
                        m_Property.SetTexture(FPageShaderID.AlbedoTexture[index - 1], terrainLayer.diffuseTexture);
                        m_Property.SetTexture(FPageShaderID.NormalTexture[index - 1], terrainLayer.normalMapTexture);
                        ++index;
                    }

                    cmdBuffer.DrawMesh(m_DrawPageMesh, Matrix4x4.identity, m_DrawColorMaterial, 0, layerIndex <= 4 ? 0 : 1, m_Property);
                }
            }
        }

        public void Dispose()
        {
            m_DrawInfos.Dispose();
            loadRequests.Dispose();
            m_PageTableBuffer.Dispose();
            Object.DestroyImmediate(m_DrawPageMesh);
            Object.DestroyImmediate(m_TriangleMesh);
            Object.DestroyImmediate(m_DrawPageMaterial);
            Object.DestroyImmediate(m_DrawColorMaterial);
        }
    }
}
