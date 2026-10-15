using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Landscape.RuntimeVirtualTexture
{
    internal struct FDrawPageParameter
    {
        public Rect volumeRect;
        public Terrain[] terrainList;
    }

    internal static class FPageShaderID
    {
        public static int SplatTileOffset = Shader.PropertyToID("_SplatTileOffset");
        public static int SplatTexture = Shader.PropertyToID("_SplatTexture");
        public static int SurfaceTileOffset = Shader.PropertyToID("_SurfaceTileOffset");
        public static int[] AlbedoTexture = new int[4] { Shader.PropertyToID("_AlbedoTexture1"), Shader.PropertyToID("_AlbedoTexture2"), Shader.PropertyToID("_AlbedoTexture3"), Shader.PropertyToID("_AlbedoTexture4") };
        public static int[] NormalTexture = new int[4] { Shader.PropertyToID("_NormalTexture1"), Shader.PropertyToID("_NormalTexture2"), Shader.PropertyToID("_NormalTexture3"), Shader.PropertyToID("_NormalTexture4") };
        public static int Size = Shader.PropertyToID("_Size");
        public static int SrcTexture = Shader.PropertyToID("_SrcTexture");
        public static int DscTexture = Shader.PropertyToID("_DscTexture");   
    }

    internal unsafe class FPageRenderer : IDisposable
    {
        private Mesh m_DrawPageMesh;
        private Material m_DrawColorMaterial;
        private MaterialPropertyBlock m_Property;

        public FPageRenderer()
        {
            this.m_Property = new MaterialPropertyBlock();
            this.m_DrawPageMesh = BuildQuadMesh();
            this.m_DrawColorMaterial = new Material(Shader.Find("VirtualTexture/DrawPageColor"));
        }

        public void BakePages(CommandBuffer cmdBuffer, NativeList<BakeOrder> bakes, PhysicalPool* pool, VirtualTextureAsset virtualTexture, in FDrawPageParameter drawPageParameter)
        {
            for (int i = 0; i < bakes.Length; ++i)
            {
                int2 slotCoord = pool->SlotCoord(bakes[i].slot);
                RectInt pageRect = new RectInt(slotCoord.x * virtualTexture.TileSizePadding, slotCoord.y * virtualTexture.TileSizePadding, virtualTexture.TileSizePadding, virtualTexture.TileSizePadding);
                RenderPage(cmdBuffer, virtualTexture, pageRect, bakes[i].page, drawPageParameter);
            }
        }

        private void RenderPage(CommandBuffer cmdBuffer, VirtualTextureAsset virtualTexture, in RectInt pageRect, in PageId page, in FDrawPageParameter drawPageParameter)
        {
            int perSize = 1 << page.mip;
            int x = page.x << page.mip;
            int y = page.y << page.mip;
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
                Rect offsetRect = new Rect(pageRect.x + (maxRect.xMin - volumeRect.xMin) * scaleFactor, pageRect.y + (maxRect.yMin - volumeRect.yMin) * scaleFactor, maxRect.width * scaleFactor, maxRect.height * scaleFactor);
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

        static Mesh BuildQuadMesh()
        {
            var mesh = new Mesh();
            mesh.SetVertices(new[] { new Vector3(0, 1, 0.1f), new Vector3(0, 0, 0.1f), new Vector3(1, 0, 0.1f), new Vector3(1, 1, 0.1f) });
            mesh.SetUVs(0, new[] { new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1) });
            mesh.SetTriangles(new[] { 0, 1, 2, 2, 3, 0 }, 0);
            return mesh;
        }

        public void Dispose()
        {
            Object.DestroyImmediate(m_DrawPageMesh);
            Object.DestroyImmediate(m_DrawColorMaterial);
        }
    }
}
