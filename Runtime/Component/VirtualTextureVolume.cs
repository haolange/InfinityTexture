using UnityEngine;
using Unity.Mathematics;

namespace Landscape.RuntimeVirtualTexture
{
    public enum EVirtualTextureVolumeSize
    {
        X128 = 128,
        X256 = 256,
        X512 = 512,
        X1024 = 1024,
        X2048 = 2048,
    }

    public unsafe class VirtualTextureVolume : MonoBehaviour
    {
        [HideInInspector]
        public Material m_material;
        private Terrain[] m_terrainList;

        [Header("Texture")]
        public EVirtualTextureVolumeSize volumeScale;
        public VirtualTextureAsset virtualTexture;

        private float m_CellSize
        {
            get
            {
                return VolumeSize / virtualTexture.pageSize;
            }
        }
        public float VolumeSize
        {
            get
            {
                return (int)volumeScale;
            }
        }
        private float m_VolumeRadius
        {
            get
            {
                return VolumeSize * 0.5f;
            }
        }

        internal FRect volumeRect;
        internal FPageProducer pageProducer;
        internal FPageRenderer pageRenderer;
        internal static VirtualTextureVolume s_VirtualTextureVolume;


        void OnEnable()
        {
            SetTerrainMaterial();

            virtualTexture.Initialize();
            s_VirtualTextureVolume = this;

            int2 fixedCenter = GetFixedCenter(GetFixedPos(transform.position));
            volumeRect = new FRect(fixedCenter.x - m_VolumeRadius, fixedCenter.y - m_VolumeRadius, VolumeSize, VolumeSize);
            Shader.SetGlobalVector("_VTVolumeRect", new Vector4(volumeRect.xMin, volumeRect.yMin, volumeRect.width, volumeRect.height));
            Shader.SetGlobalVector("_VTVolumeBound", new Vector4(transform.position.x, transform.position.y, transform.position.z, VolumeSize));

            pageProducer = new FPageProducer(virtualTexture.pageSize, virtualTexture.NumMip);
            pageRenderer = new FPageRenderer(virtualTexture.pageSize, virtualTexture.NumMip);
        }

        void OnDisable()
        {
            pageProducer.Dispose();
            pageRenderer.Dispose();
            virtualTexture.Dispose();
        }

        void SetTerrainMaterial()
        {
            m_terrainList = GameObject.FindObjectsOfType<Terrain>();
            if (m_terrainList.Length == 0) { return; }

            for (int i = 0; i < m_terrainList.Length; i++)
            {
                m_terrainList[i].materialTemplate = m_material;
            }
        }

        int2 GetFixedCenter(int2 pos)
        {
            return new int2((int)Mathf.Floor(pos.x / m_VolumeRadius + 0.5f) * (int)m_VolumeRadius, (int)Mathf.Floor(pos.y / m_VolumeRadius + 0.5f) * (int)m_VolumeRadius);
        }

        int2 GetFixedPos(Vector3 pos)
        {
            return new int2((int)Mathf.Floor(pos.x / m_CellSize + 0.5f) * (int)m_CellSize, (int)Mathf.Floor(pos.z / m_CellSize + 0.5f) * (int)m_CellSize);
        }

        internal FDrawPageParameter GetDrawPageParamter()
        {
            FDrawPageParameter drawPageParameter;
            drawPageParameter.volumeRect = volumeRect;
            drawPageParameter.terrainList = m_terrainList;
            return drawPageParameter;
        }
    }
}