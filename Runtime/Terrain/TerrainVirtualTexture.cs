using UnityEngine;

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

        public float size => (int)volumeSize;

        void OnEnable()
        {
            if (virtualTexture == null || m_TerrainMaterial == null || m_BakeShader == null)
            {
                Debug.LogError($"{name}: 缺少 VirtualTextureAsset、地形材质或烘焙 shader 引用", this);
                return;
            }
            if (!virtualTexture.Validate(out string error))
            {
                Debug.LogError($"{name}: {error}", virtualTexture);
                return;
            }

            Terrain[] terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.InstanceID);
            foreach (Terrain terrain in terrains) { terrain.materialTemplate = m_TerrainMaterial; }

            Rect volumeRect = VolumeRect();
            Shader.SetGlobalVector(s_VolumeRectID, new Vector4(volumeRect.x, volumeRect.y, volumeRect.width, volumeRect.height));

            m_Producer = new TerrainPageProducer(terrains, volumeRect, m_BakeShader, virtualTexture.layers);
            m_System = new VirtualTextureSystem(virtualTexture, m_Producer);
        }

        void OnDisable()
        {
            m_System?.Dispose();
            m_Producer?.Dispose();
            m_System = null;
            m_Producer = null;
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
