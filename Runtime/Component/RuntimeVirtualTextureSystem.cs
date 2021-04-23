using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using System.Collections.Generic;

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
        public int2 FeedbackSize = new int2(1920, 1080);
        [HideInInspector]
        public GameObject m_feedbackPrefab;
        public EFeedbackScale FeedbackScale = EFeedbackScale.X16;

        [Header("Texture")]
        public VirtualTextureAsset virtualTextureAsset;

        private Camera m_playerCamera;
        private Camera m_feedbackCamera;
        private GameObject m_feedbackObject;

        private Rect VTVolumeParams;
        private FPageProducer pageProducer;
        private FPageRenderer pageRenderer;
        private FeedbackReader feedbackReader;
        private FeedbackRenderer feedbackRenderer;


        private void OnEnable()
        {
            SetFeedbackCamera();
            SetTerrainMaterial();

            int2 fixedCenter = GetFixedCenter(GetFixedPos(transform.position));
            VTVolumeParams = new Rect(fixedCenter.x - VolumeRadius, fixedCenter.y - VolumeRadius, VolumeSize, VolumeSize);
            Shader.SetGlobalVector("_VTVolumeParams", new Vector4(VTVolumeParams.xMin, VTVolumeParams.yMin, VTVolumeParams.width, VTVolumeParams.height));

            feedbackReader = new FeedbackReader();
            feedbackRenderer = new FeedbackRenderer();
            pageProducer = new FPageProducer(virtualTextureAsset.pageSize, virtualTextureAsset.NumMip);
            pageRenderer = new FPageRenderer(virtualTextureAsset.pageSize, virtualTextureAsset.NumMip);

            virtualTextureAsset.Initialize();
            feedbackRenderer.Initialize(m_playerCamera, m_feedbackCamera, FeedbackSize, FeedbackScale, virtualTextureAsset);
        }

        private void Update()
        {
            if (CheckRunSystem()) { return; }

            feedbackReader.ProcessAndDrawPageTable(pageRenderer, pageProducer, virtualTextureAsset);

            if (feedbackReader.bReady)
            {
                feedbackRenderer.FeedbackCamera.Render();
                feedbackReader.RequestReadback(feedbackRenderer.TargetTexture);
            }

            pageRenderer.DrawPageColor(this, pageProducer, ref virtualTextureAsset.lruCache[0], virtualTextureAsset.tileNum, virtualTextureAsset.TileSizePadding);
        }

        public void DrawMesh(CommandBuffer cmdBuffer, Mesh quadMesh, Material pageColorMat, MaterialPropertyBlock propertyBlock, in FRectInt pageCoordRect, in FPageRequestInfo requestInfo)
        {
            int x = requestInfo.pageX;
            int y = requestInfo.pageY;
            int perSize = (int)Mathf.Pow(2, requestInfo.mipLevel);
            x = x - x % perSize;
            y = y - y % perSize;

            var tableSize = virtualTextureAsset.pageSize;

            var paddingEffect = virtualTextureAsset.tileBorder * perSize * (VTVolumeParams.width / tableSize) / virtualTextureAsset.tileSize;

            var realRect = new Rect(VTVolumeParams.xMin + (float)x / tableSize * VTVolumeParams.width - paddingEffect,
                                    VTVolumeParams.yMin + (float)y / tableSize * VTVolumeParams.height - paddingEffect,
                                    VTVolumeParams.width / tableSize * perSize + 2f * paddingEffect,
                                    VTVolumeParams.width / tableSize * perSize + 2f * paddingEffect);


            var terRect = Rect.zero;
            foreach (var terrain in m_terrains)
            {
                propertyBlock.Clear();

                terRect.xMin = terrain.transform.position.x;
                terRect.yMin = terrain.transform.position.z;
                terRect.width = terrain.terrainData.size.x;
                terRect.height = terrain.terrainData.size.z;
                
                if ( !realRect.Overlaps(terRect) ) { continue; }

                var needDrawRect = realRect;
                needDrawRect.xMin = Mathf.Max(realRect.xMin, terRect.xMin);
                needDrawRect.yMin = Mathf.Max(realRect.yMin, terRect.yMin);
                needDrawRect.xMax = Mathf.Min(realRect.xMax, terRect.xMax);
                needDrawRect.yMax = Mathf.Min(realRect.yMax, terRect.yMax);

                var scaleFactor = pageCoordRect.width / realRect.width;

                var position = new Rect(pageCoordRect.x + (needDrawRect.xMin - realRect.xMin) * scaleFactor,
                                        pageCoordRect.y + (needDrawRect.yMin - realRect.yMin) * scaleFactor,
                                        needDrawRect.width * scaleFactor,
                                        needDrawRect.height * scaleFactor);

                var scaleOffset = new Vector4(needDrawRect.width / terRect.width, 
                                              needDrawRect.height / terRect.height,
                                              (needDrawRect.xMin - terRect.xMin) / terRect.width,
                                              (needDrawRect.yMin - terRect.yMin) / terRect.height);
                // 构建变换矩阵
                float l = position.x * 2.0f / virtualTextureAsset.TextureSize - 1;
                float r = (position.x + position.width) * 2.0f / virtualTextureAsset.TextureSize - 1;
                float b = position.y * 2.0f / virtualTextureAsset.TextureSize - 1;
                float t = (position.y + position.height) * 2.0f / virtualTextureAsset.TextureSize - 1;
                Matrix4x4 Matrix_MVP = new Matrix4x4();
                Matrix_MVP.m00 = r - l;
                Matrix_MVP.m03 = l;
                Matrix_MVP.m11 = t - b;
                Matrix_MVP.m13 = b;
                Matrix_MVP.m23 = -1;
                Matrix_MVP.m33 = 1;

                // 绘制贴图
                propertyBlock.SetVector("_SplatTileOffset", scaleOffset);
                propertyBlock.SetMatrix(Shader.PropertyToID("_Matrix_MVP"), GL.GetGPUProjectionMatrix(Matrix_MVP, true));

                int layerIndex = 0;
                for (int i = 0; i < terrain.terrainData.alphamapTextures.Length; ++i)
                {
                    var splatMap = terrain.terrainData.alphamapTextures[i];
                    propertyBlock.SetTexture("_SplatTexture", splatMap);

                    int index = 1;
                    for(;layerIndex < terrain.terrainData.terrainLayers.Length && index <= 4;layerIndex ++)
                    {
                        var layer = terrain.terrainData.terrainLayers[layerIndex];
                        var tileScale = new Vector2(terrain.terrainData.size.x / layer.tileSize.x, terrain.terrainData.size.z / layer.tileSize.y);
                        var tileOffset = new Vector4(tileScale.x * scaleOffset.x, tileScale.y * scaleOffset.y, scaleOffset.z * tileScale.x, scaleOffset.w * tileScale.y);
                        propertyBlock.SetVector("_SurfaceTileOffset", tileOffset);
                        propertyBlock.SetTexture($"_AlbedoTexture{index}", layer.diffuseTexture);
                        propertyBlock.SetTexture($"_NormalTexture{index}", layer.normalMapTexture);
                        index++;
                    }

                    cmdBuffer.DrawMesh(quadMesh, Matrix4x4.identity, pageColorMat, 0, layerIndex <= 4 ? 0 : 1, propertyBlock);
                }
            }
        }

        public void SetFeedbackCamera()
        {
            m_playerCamera = Camera.main;
            m_feedbackObject = GameObject.Instantiate(m_feedbackPrefab, m_playerCamera.transform.position, m_playerCamera.transform.rotation);
            m_feedbackObject.transform.parent = m_playerCamera.transform;
            m_feedbackCamera = m_feedbackObject.GetComponent<Camera>();
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
            if (m_terrains.Length == 0 && m_playerCamera == null && m_feedbackCamera == null && virtualTextureAsset == null) { return true; }

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

            pageProducer.Dispose();
            pageRenderer.Dispose();
            feedbackRenderer.Dispose();
            virtualTextureAsset.Dispose();
            Object.DestroyImmediate(m_feedbackObject, true);
        }
    }
}