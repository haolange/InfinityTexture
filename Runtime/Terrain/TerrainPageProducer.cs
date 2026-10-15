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
        static readonly int[] s_AlbedoIDs = LayerIDs("_AlbedoTexture");
        static readonly int[] s_NormalIDs = LayerIDs("_NormalTexture");
        static readonly int[] s_MaskIDs = LayerIDs("_MaskTexture");
        static readonly int[] s_LayerParamsIDs = LayerIDs("_LayerParams");
        static readonly int[] s_LayerTileOffsetIDs = LayerIDs("_LayerTileOffset");
        static readonly int[] s_MaskRemapScaleIDs = LayerIDs("_MaskRemapScale");
        static readonly int[] s_MaskRemapOffsetIDs = LayerIDs("_MaskRemapOffset");

        const int FirstSplatPass = 0;
        const int AddSplatPass = 1;

        readonly Terrain[] m_Terrains;
        readonly Rect m_VolumeRect;
        readonly Material m_Material;
        readonly MaterialPropertyBlock m_Properties = new MaterialPropertyBlock();

        public TerrainPageProducer(Terrain[] terrains, Rect volumeRect, Shader bakeShader, EPageLayers layers)
        {
            m_Terrains = terrains;
            m_VolumeRect = volumeRect;
            m_Material = new Material(bakeShader);
            if (layers == EPageLayers.BaseColorNormal) { m_Material.EnableKeyword("_PAGE_NORMAL"); }
            if (layers == EPageLayers.BaseColorNormalMask) { m_Material.EnableKeyword("_PAGE_NORMAL_MASK"); }
        }

        static int[] LayerIDs(string prefix)
        {
            var ids = new int[4];
            for (int i = 0; i < 4; ++i) { ids[i] = Shader.PropertyToID(prefix + (i + 1)); }
            return ids;
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
                    SetLayer(channel, layers[layerIndex], terrainData, splatTileOffset);
                }
                cmd.DrawProcedural(Matrix4x4.identity, m_Material, splat == 0 ? FirstSplatPass : AddSplatPass, MeshTopology.Triangles, 6, 1, m_Properties);
            }
        }

        /// <summary>按 URP 地形的约定传入一个地形层：贴图、平铺、metallic / smoothness、mask remap、法线强度。</summary>
        void SetLayer(int channel, TerrainLayer layer, TerrainData terrainData, float4 splatTileOffset)
        {
            // 地形 uv → 图层 uv：× (地形尺寸 / 平铺尺寸) + 平铺偏移 / 平铺尺寸；再与 scratch → 地形 uv 复合。
            var tileSize = new float2(layer.tileSize.x, layer.tileSize.y);
            float2 tileScale = new float2(terrainData.size.x, terrainData.size.z) / tileSize;
            float2 tileOffset = new float2(layer.tileOffset.x, layer.tileOffset.y) / tileSize;
            m_Properties.SetVector(s_LayerTileOffsetIDs[channel], new float4(splatTileOffset.xy * tileScale, splatTileOffset.zw * tileScale + tileOffset));

            bool hasMask = layer.maskMapTexture != null;
            Vector4 remapScale = layer.maskMapRemapMax - layer.maskMapRemapMin;

            m_Properties.SetTexture(s_AlbedoIDs[channel], layer.diffuseTexture != null ? layer.diffuseTexture : Texture2D.whiteTexture);
            m_Properties.SetTexture(s_NormalIDs[channel], layer.normalMapTexture != null ? layer.normalMapTexture : Texture2D.normalTexture);
            m_Properties.SetTexture(s_MaskIDs[channel], hasMask ? layer.maskMapTexture : Texture2D.whiteTexture);
            m_Properties.SetVector(s_LayerParamsIDs[channel], new Vector4(layer.metallic, layer.smoothness, hasMask ? 1 : 0, layer.normalScale));
            m_Properties.SetVector(s_MaskRemapScaleIDs[channel], remapScale);
            m_Properties.SetVector(s_MaskRemapOffsetIDs[channel], layer.maskMapRemapMin);
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
