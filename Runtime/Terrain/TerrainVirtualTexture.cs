using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace Landscape.RuntimeVirtualTexture
{
    public enum EVirtualTextureVolumeSize
    {
        X128 = 128,
        X256 = 256,
        X512 = 512,
        X1024 = 1024,
        X2048 = 2048,
        X4096 = 4096,
    }

    /// <summary>
    /// 场景里的地形虚拟纹理：确定体积矩形（虚拟 uv 的世界范围），
    /// 把场景地形换成 VT 材质，并以地形为唯一生产者启动一套 VirtualTextureSystem。
    /// </summary>
    public class TerrainVirtualTexture : MonoBehaviour
    {
        static readonly int s_VolumeRectID = Shader.PropertyToID("_VTVolumeRect");

        [Header("Texture")]
        public EVirtualTextureVolumeSize volumeSize = EVirtualTextureVolumeSize.X1024;
        public VirtualTextureAsset virtualTexture;

        [SerializeField, HideInInspector] Material m_TerrainMaterial;
        [SerializeField, HideInInspector] Shader m_BakeShader;

        TerrainPageProducer m_Producer;
        VirtualTextureSystem m_System;
        Terrain[] m_Terrains;
        Material[] m_OriginalMaterials;
        float[] m_OriginalBasemapDistances;
        bool m_UsingVirtualTexture;

        public float size => (int)volumeSize;

        void OnEnable()
        {
            if (virtualTexture == null || m_TerrainMaterial == null || m_BakeShader == null)
            {
                Debug.LogError($"{name}: 缺少 VirtualTextureAsset、地形材质或烘焙 shader 引用", this);
                return;
            }
            if (!VirtualTextureSystem.CanCreate(virtualTexture, out string error))
            {
                Debug.LogError($"{name}: {error}", virtualTexture);
                return;
            }
            if (VirtualTextureSystem.active != null)
            {
                Debug.LogError($"{name}: 已有启用的虚拟纹理体积", this);
                return;
            }

            Rect volumeRect = VolumeRect();
            var covered = new List<Terrain>();
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                Vector3 position = terrain.transform.position;
                Vector3 terrainSize = terrain.terrainData.size;
                var bounds = new Rect(position.x, position.z, terrainSize.x, terrainSize.z);
                if (Covers(volumeRect, bounds)) { covered.Add(terrain); }
                else if (volumeRect.Overlaps(bounds))
                {
                    Debug.LogWarning($"{terrain.name}: 地形未完整包含在 VT 体积内，保持原始材质", terrain);
                }
            }
            if (covered.Count == 0) { return; }
            Terrain[] terrains = covered.ToArray();
            Shader.SetGlobalVector(s_VolumeRectID, new Vector4(volumeRect.x, volumeRect.y, volumeRect.width, volumeRect.height));

            m_Producer = new TerrainPageProducer(terrains, volumeRect, m_BakeShader, virtualTexture.layers);
            try { m_System = new VirtualTextureSystem(virtualTexture, m_Producer); }
            catch
            {
                m_Producer.Dispose();
                m_Producer = null;
                throw;
            }

            m_Terrains = terrains;
            m_OriginalMaterials = new Material[terrains.Length];
            m_OriginalBasemapDistances = new float[terrains.Length];
            for (int i = 0; i < terrains.Length; ++i)
            {
                Material original = terrains[i].materialTemplate;
                if (original == m_TerrainMaterial)
                {
                    original = GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial;
                    terrains[i].materialTemplate = original;
                }
                m_OriginalMaterials[i] = original;
                m_OriginalBasemapDistances[i] = terrains[i].basemapDistance;
            }
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
        }

        internal static bool Covers(Rect volume, Rect bounds) =>
            bounds.xMin >= volume.xMin && bounds.yMin >= volume.yMin &&
            bounds.xMax <= volume.xMax && bounds.yMax <= volume.yMax;

        void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            bool useVirtualTexture = camera.cameraType == CameraType.Game && m_System != null && VirtualTextureSystem.active == m_System && m_System.rootReady;
            if (useVirtualTexture == m_UsingVirtualTexture) { return; }
            m_UsingVirtualTexture = useVirtualTexture;
            if (!useVirtualTexture) { RestoreMaterials(); return; }
            for (int i = 0; i < m_Terrains.Length; ++i)
            {
                Terrain terrain = m_Terrains[i];
                if (terrain == null) { continue; }
                // 远处同样使用 VT，不能让 Terrain 切回原生 basemap 绘制。
                terrain.basemapDistance = float.MaxValue;
                terrain.materialTemplate = m_TerrainMaterial;
            }
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            m_System?.Dispose();
            m_Producer?.Dispose();
            m_System = null;
            m_Producer = null;
            RestoreMaterials();
            m_UsingVirtualTexture = false;
            m_Terrains = null;
            m_OriginalMaterials = null;
            m_OriginalBasemapDistances = null;
        }

        void RestoreMaterials()
        {
            if (m_Terrains != null)
            {
                for (int i = 0; i < m_Terrains.Length; ++i)
                {
                    Terrain terrain = m_Terrains[i];
                    if (terrain == null) { continue; }
                    if (terrain.materialTemplate == m_TerrainMaterial)
                    {
                        terrain.materialTemplate = m_OriginalMaterials[i];
                    }
                    if (terrain.basemapDistance == float.MaxValue)
                    {
                        terrain.basemapDistance = m_OriginalBasemapDistances[i];
                    }
                }
            }
        }

        /// <summary>体积中心按半个体积对齐，避免移动组件时虚拟 uv 出现亚页偏移。</summary>
        Rect VolumeRect()
        {
            float half = size * 0.5f;
            Vector3 position = transform.position;
            float centerX = Mathf.Floor(position.x / half + 0.5f) * half;
            float centerZ = Mathf.Floor(position.z / half + 0.5f) * half;
            return new Rect(centerX - half, centerZ - half, size, size);
        }
    }
}
