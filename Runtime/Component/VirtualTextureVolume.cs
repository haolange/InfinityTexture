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

        [Header("Feedback")]
        [HideInInspector]
        public GameObject m_FeedbackPrefab;
        public int2 feedbackSize = new int2(1920, 1080);
        public EFeedbackScale feedbackScale = EFeedbackScale.X16;

        [Header("Texture")]
        public EVirtualTextureVolumeSize volumeScale;
        public VirtualTextureAsset virtualTextureAsset;

        private float m_CellSize
        {
            get
            {
                return VolumeSize / virtualTextureAsset.pageSize;
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
        private FRect m_VolumeRect;
        private Camera m_PlayerCamera;
        private Camera m_FeedbackCamera;
        private GameObject m_FeedbackObject;
        private FPageProducer m_PageProducer;
        private FPageRenderer m_PageRenderer;
        private FeedbackReader m_FeedbackReader;
        private FeedbackRenderer m_FeedbackRenderer;


        void OnEnable()
        {
            SetFeedbackCamera();
            SetTerrainMaterial();

            int2 fixedCenter = GetFixedCenter(GetFixedPos(transform.position));
            m_VolumeRect = new FRect(fixedCenter.x - m_VolumeRadius, fixedCenter.y - m_VolumeRadius, VolumeSize, VolumeSize);
            Shader.SetGlobalVector("_VTVolumeRect", new Vector4(m_VolumeRect.xMin, m_VolumeRect.yMin, m_VolumeRect.width, m_VolumeRect.height));

            m_FeedbackReader = new FeedbackReader();
            m_FeedbackRenderer = new FeedbackRenderer();
            m_PageProducer = new FPageProducer(virtualTextureAsset.pageSize, virtualTextureAsset.NumMip);
            m_PageRenderer = new FPageRenderer(virtualTextureAsset.pageSize, virtualTextureAsset.NumMip);

            virtualTextureAsset.Initialize();
            m_FeedbackRenderer.Initialize(m_PlayerCamera, m_FeedbackCamera, feedbackSize, feedbackScale, virtualTextureAsset);
        }

        void Update()
        {
            m_FeedbackReader.ProcessAndDrawPageTable(m_PageProducer, m_PageRenderer, virtualTextureAsset);

            if (m_FeedbackReader.bReady)
            {
                m_FeedbackRenderer.FeedbackCamera.Render();
                m_FeedbackReader.RequestReadback(m_FeedbackRenderer.TargetTexture);
            }

            FDrawPageParameter drawPageParameter;
            drawPageParameter.volumeRect = m_VolumeRect;
            drawPageParameter.terrainList = m_terrainList;
            m_PageRenderer.DrawPageColor(m_PageProducer, virtualTextureAsset, ref virtualTextureAsset.lruCache[0], drawPageParameter);
        }

        void OnDisable()
        {
            m_PageProducer.Dispose();
            m_PageRenderer.Dispose();
            m_FeedbackRenderer.Dispose();
            virtualTextureAsset.Dispose();
            Object.DestroyImmediate(m_FeedbackObject, true);
        }

        void SetFeedbackCamera()
        {
            m_PlayerCamera = Camera.main;
            m_FeedbackObject = GameObject.Instantiate(m_FeedbackPrefab, m_PlayerCamera.transform.position, m_PlayerCamera.transform.rotation);
            m_FeedbackObject.transform.parent = m_PlayerCamera.transform;
            m_FeedbackCamera = m_FeedbackObject.GetComponent<Camera>();
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
    }
}