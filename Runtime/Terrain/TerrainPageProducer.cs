using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>
    /// 地形生产者：虚拟 uv ↔ 世界 XZ 由体积矩形决定；
    /// 烘焙时把与页相交的每块地形的 splat 混合结果画进 scratch。
    /// </summary>
    internal class TerrainPageProducer : IPageProducer, IDisposable
    {
        static readonly int s_BakeMatrixID = Shader.PropertyToID("_BakeMatrix");
        static readonly int s_SplatTextureID = Shader.PropertyToID("_SplatTexture");
        static readonly int s_SplatTileOffsetID = Shader.PropertyToID("_SplatTileOffset");
        static readonly int s_SurfaceTileOffsetID = Shader.PropertyToID("_SurfaceTileOffset");
        static readonly int[] s_AlbedoIDs = { Shader.PropertyToID("_AlbedoTexture1"), Shader.PropertyToID("_AlbedoTexture2"), Shader.PropertyToID("_AlbedoTexture3"), Shader.PropertyToID("_AlbedoTexture4") };
        static readonly int[] s_NormalIDs = { Shader.PropertyToID("_NormalTexture1"), Shader.PropertyToID("_NormalTexture2"), Shader.PropertyToID("_NormalTexture3"), Shader.PropertyToID("_NormalTexture4") };

        const int FirstSplatPass = 0;
        const int AddSplatPass = 1;

        readonly Terrain[] m_Terrains;
        readonly Rect m_VolumeRect;
        readonly Material m_Material;
        readonly MaterialPropertyBlock m_Properties = new MaterialPropertyBlock();

        public TerrainPageProducer(Terrain[] terrains, Rect volumeRect, Shader bakeShader)
        {
            m_Terrains = terrains;
            m_VolumeRect = volumeRect;
            m_Material = new Material(bakeShader);
        }

        /// <summary>虚拟 uv 矩形 → 世界 XZ 矩形。</summary>
        public Rect ToWorld(float4 uvRect)
        {
            return new Rect(m_VolumeRect.x + uvRect.x * m_VolumeRect.width, m_VolumeRect.y + uvRect.y * m_VolumeRect.height, uvRect.z * m_VolumeRect.width, uvRect.w * m_VolumeRect.height);
        }

        public void Bake(CommandBuffer cmd, in PageBakeRequest request)
        {
            Rect pageRect = ToWorld(request.uvRect);

            foreach (Terrain terrain in m_Terrains)
            {
                TerrainData terrainData = terrain.terrainData;
                Vector3 position = terrain.transform.position;
                var terrainRect = new Rect(position.x, position.z, terrainData.size.x, terrainData.size.z);
                if (!pageRect.Overlaps(terrainRect)) { continue; }

                Rect overlap = Rect.MinMaxRect(
                    Mathf.Max(pageRect.xMin, terrainRect.xMin), Mathf.Max(pageRect.yMin, terrainRect.yMin),
                    Mathf.Min(pageRect.xMax, terrainRect.xMax), Mathf.Min(pageRect.yMax, terrainRect.yMax));

                // 相交区域在 scratch 上的位置（0..1），以及在这块地形 splat 上的位置。
                Rect target = Rect.MinMaxRect(
                    (overlap.xMin - pageRect.xMin) / pageRect.width, (overlap.yMin - pageRect.yMin) / pageRect.height,
                    (overlap.xMax - pageRect.xMin) / pageRect.width, (overlap.yMax - pageRect.yMin) / pageRect.height);
                var splatTileOffset = new float4(overlap.width / terrainRect.width, overlap.height / terrainRect.height, (overlap.xMin - terrainRect.xMin) / terrainRect.width, (overlap.yMin - terrainRect.yMin) / terrainRect.height);

                m_Properties.Clear();
                m_Properties.SetMatrix(s_BakeMatrixID, BakeMatrix(target));
                m_Properties.SetVector(s_SplatTileOffsetID, splatTileOffset);
                DrawSplats(cmd, terrainData, splatTileOffset);
            }
        }

        /// <summary>每张 splat 控制图画一次：第一张直接写，后面的叠加。</summary>
        void DrawSplats(CommandBuffer cmd, TerrainData terrainData, float4 splatTileOffset)
        {
            TerrainLayer[] layers = terrainData.terrainLayers;
            int layerIndex = 0;
            for (int splat = 0; splat < terrainData.alphamapTextureCount; ++splat)
            {
                m_Properties.SetTexture(s_SplatTextureID, terrainData.GetAlphamapTexture(splat));
                for (int channel = 0; channel < 4 && layerIndex < terrainData.alphamapLayers; ++channel, ++layerIndex)
                {
                    TerrainLayer layer = layers[layerIndex];
                    var tileScale = new float2(terrainData.size.x / layer.tileSize.x, terrainData.size.z / layer.tileSize.y);
                    m_Properties.SetVector(s_SurfaceTileOffsetID, new float4(tileScale * splatTileOffset.xy, tileScale * splatTileOffset.zw));
                    m_Properties.SetTexture(s_AlbedoIDs[channel], layer.diffuseTexture);
                    m_Properties.SetTexture(s_NormalIDs[channel], layer.normalMapTexture != null ? layer.normalMapTexture : Texture2D.normalTexture);
                }
                cmd.DrawProcedural(Matrix4x4.identity, m_Material, splat == 0 ? FirstSplatPass : AddSplatPass, MeshTopology.Triangles, 6, 1, m_Properties);
            }
        }

        /// <summary>把单位矩形映射到 scratch 上的 target 区域（裁剪空间），按平台翻转 y。</summary>
        static Matrix4x4 BakeMatrix(Rect target)
        {
            var matrix = Matrix4x4.identity;
            matrix.m00 = target.width * 2;
            matrix.m03 = target.xMin * 2 - 1;
            matrix.m11 = target.height * 2;
            matrix.m13 = target.yMin * 2 - 1;
            return GL.GetGPUProjectionMatrix(matrix, true);
        }

        public void Dispose()
        {
            Object.Destroy(m_Material);
        }
    }
}
