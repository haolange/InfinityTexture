using UnityEngine;
using Unity.Mathematics;

namespace Landscape.RuntimeVirtualTexture
{
    [RequireComponent(typeof(RuntimeVirtualTextureVolume))]
    internal unsafe class RuntimeVirtualTextureSystem : MonoBehaviour
    {
        [HideInInspector]
        public float VolumeSize = 1024;
        private float VolumeRadius
        {
            get
            {
                return VolumeSize * 0.5f;
            }
        }
        private float CellSize
        {
            get
            {
                return VolumeSize / virtualTextureAsset.pageSize;
            }
        }

        [HideInInspector]
        public Material m_material;
        private Terrain[] m_terrains;

        [Header("Feedback")]
        [HideInInspector]
        public GameObject m_feedbackPrefab;
        public int2 FeedbackSize = new int2(1920, 1080);
        public EFeedbackScale FeedbackScale = EFeedbackScale.X16;

        [Header("Texture")]
        public VirtualTextureAsset virtualTextureAsset;

        private Camera m_PlayerCamera;
        private Camera m_FeedbackCamera;
        private GameObject m_FeedbackObject;

        private FRect m_VolumeRect;
        private FPageProducer m_PageProducer;
        private FPageRenderer m_PageRenderer;
        private FeedbackReader m_FeedbackReader;
        private FeedbackRenderer m_FeedbackRenderer;

        //
        private void OnEnable()
        {
            SetFeedbackCamera();
            SetTerrainMaterial();

            int2 fixedCenter = GetFixedCenter(GetFixedPos(transform.position));
            m_VolumeRect = new FRect(fixedCenter.x - VolumeRadius, fixedCenter.y - VolumeRadius, VolumeSize, VolumeSize);
            Shader.SetGlobalVector("_VTVolumeParams", new Vector4(m_VolumeRect.xMin, m_VolumeRect.yMin, m_VolumeRect.width, m_VolumeRect.height));

            m_FeedbackReader = new FeedbackReader();
            m_FeedbackRenderer = new FeedbackRenderer();
            m_PageProducer = new FPageProducer(virtualTextureAsset.pageSize, virtualTextureAsset.NumMip);
            m_PageRenderer = new FPageRenderer(virtualTextureAsset.pageSize, virtualTextureAsset.NumMip);

            virtualTextureAsset.Initialize();
            m_FeedbackRenderer.Initialize(m_PlayerCamera, m_FeedbackCamera, FeedbackSize, FeedbackScale, virtualTextureAsset);
        }

        private void Update()
        {
            if (CheckRunSystem()) { return; }

            m_FeedbackReader.ProcessAndDrawPageTable(m_PageProducer, m_PageRenderer, virtualTextureAsset);

            if (m_FeedbackReader.bReady)
            {
                m_FeedbackRenderer.FeedbackCamera.Render();
                m_FeedbackReader.RequestReadback(m_FeedbackRenderer.TargetTexture);
            }

            FDrawPageParameter drawPageParameter;
            drawPageParameter.terrainList = m_terrains;
            drawPageParameter.volumeRect = m_VolumeRect;
            m_PageRenderer.DrawPageColor(m_PageProducer, virtualTextureAsset, ref virtualTextureAsset.lruCache[0], drawPageParameter);
        }

        public void SetFeedbackCamera()
        {
            m_PlayerCamera = Camera.main;
            m_FeedbackObject = GameObject.Instantiate(m_feedbackPrefab, m_PlayerCamera.transform.position, m_PlayerCamera.transform.rotation);
            m_FeedbackObject.transform.parent = m_PlayerCamera.transform;
            m_FeedbackCamera = m_FeedbackObject.GetComponent<Camera>();
        }

        public void SetTerrainMaterial()
        {
            m_terrains = GameObject.FindObjectsOfType<Terrain>();
            if (m_terrains.Length == 0) { return; }

            for (int i = 0; i < m_terrains.Length; i++)
            {
                m_terrains[i].materialTemplate = m_material;
            }
        }

        private bool CheckRunSystem()
        {
            if (m_terrains.Length == 0 && m_PlayerCamera == null && m_FeedbackCamera == null && virtualTextureAsset == null) { return true; }

            return false;
        }

        private int2 GetFixedCenter(int2 pos)
        {
            return new int2((int)Mathf.Floor(pos.x / VolumeRadius + 0.5f) * (int)VolumeRadius, (int)Mathf.Floor(pos.y / VolumeRadius + 0.5f) * (int)VolumeRadius);
        }

        private int2 GetFixedPos(Vector3 pos)
        {
            return new int2((int)Mathf.Floor(pos.x / CellSize + 0.5f) * (int)CellSize, (int)Mathf.Floor(pos.z / CellSize + 0.5f) * (int)CellSize);
        }

        void OnDisable()
        {
            if (CheckRunSystem()) { return; }

            m_PageProducer.Dispose();
            m_PageRenderer.Dispose();
            m_FeedbackRenderer.Dispose();
            virtualTextureAsset.Dispose();
            Object.DestroyImmediate(m_FeedbackObject, true);
        }
    }
}