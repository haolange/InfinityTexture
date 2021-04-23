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
                return VolumeSize / VirtualTextureProfile.pageSize;
            }
        }

        [Header("Terrain")]
        public Terrain[] TerrainList;
        public Material TerrainMaterial;

        [Header("FeedbackSetting")]
        public int2 FeedbackSize = new int2(1920, 1080);
        public EFeedbackScale FeedbackScale = EFeedbackScale.X16;

        [Header("VirtualTexture")]
        public Camera PlayerCamera;
        public Camera FeedbackCamera;
        public VirtualTextureAsset VirtualTextureProfile;

        private Rect VTVolumeParams;
        private FPageProducer pageProducer;
        private FPageRenderer pageRenderer;
        private FeedbackReader feedbackReader;
        private FeedbackRenderer feedbackRenderer;


        private void OnEnable()
        {
            if (CheckRunSystem()) { return; }

            for(int i = 0; i < TerrainList.Length; i++)
            {
                TerrainList[i].materialTemplate = TerrainMaterial;
            }

            int2 fixedCenter = GetFixedCenter(GetFixedPos(transform.position));
            VTVolumeParams = new Rect(fixedCenter.x - VolumeRadius, fixedCenter.y - VolumeRadius, VolumeSize, VolumeSize);
            Shader.SetGlobalVector("_VTVolumeParams", new Vector4(VTVolumeParams.xMin, VTVolumeParams.yMin, VTVolumeParams.width, VTVolumeParams.height));

            pageProducer = new FPageProducer(VirtualTextureProfile.pageSize, VirtualTextureProfile.MaxMip);
            pageRenderer = new FPageRenderer(VirtualTextureProfile.pageSize, VirtualTextureProfile.MaxMip);
            feedbackReader = new FeedbackReader();
            feedbackRenderer = new FeedbackRenderer();

            VirtualTextureProfile.Initialize();
            feedbackRenderer.Initialize(PlayerCamera, FeedbackCamera, FeedbackSize, FeedbackScale, VirtualTextureProfile);
        }

        private void Update()
        {
            if (CheckRunSystem()) { return; }

            feedbackReader.ProcessAndDrawPageTable(pageRenderer, pageProducer, VirtualTextureProfile);

            if (feedbackReader.bReady)
            {
                feedbackRenderer.FeedbackCamera.Render();
                feedbackReader.RequestReadback(feedbackRenderer.TargetTexture);
            }

            pageRenderer.DrawPageColor(this, pageProducer, ref VirtualTextureProfile.lruCache[0], VirtualTextureProfile.tileNum, VirtualTextureProfile.TileSizePadding);
        }

        public void DrawMesh(Mesh quadMesh, Material pageColorMat, in FRectInt pageCoordRect, in FPageRequestInfo requestInfo)
        {
            int x = requestInfo.pageX;
            int y = requestInfo.pageY;
            int perSize = (int)Mathf.Pow(2, requestInfo.mipLevel);
            x = x - x % perSize;
            y = y - y % perSize;

            var tableSize = VirtualTextureProfile.pageSize;

            var paddingEffect = VirtualTextureProfile.tileBorder * perSize * (VTVolumeParams.width / tableSize) / VirtualTextureProfile.tileSize;

            var realRect = new Rect(VTVolumeParams.xMin + (float)x / tableSize * VTVolumeParams.width - paddingEffect,
                                    VTVolumeParams.yMin + (float)y / tableSize * VTVolumeParams.height - paddingEffect,
                                    VTVolumeParams.width / tableSize * perSize + 2f * paddingEffect,
                                    VTVolumeParams.width / tableSize * perSize + 2f * paddingEffect);


            var terRect = Rect.zero;
            foreach (var Terrain in TerrainList)
            {
                if ( !Terrain.isActiveAndEnabled ) {
                    continue;
                }

                terRect.xMin = Terrain.transform.position.x;
                terRect.yMin = Terrain.transform.position.z;
                terRect.width = Terrain.terrainData.size.x;
                terRect.height = Terrain.terrainData.size.z;
                
                if ( !realRect.Overlaps(terRect) ) {
                    continue;
                }

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
                float l = position.x * 2.0f / VirtualTextureProfile.TextureSize - 1;
                float r = (position.x + position.width) * 2.0f / VirtualTextureProfile.TextureSize - 1;
                float b = position.y * 2.0f / VirtualTextureProfile.TextureSize - 1;
                float t = (position.y + position.height) * 2.0f / VirtualTextureProfile.TextureSize - 1;
                Matrix4x4 Matrix_MVP = new Matrix4x4();
                Matrix_MVP.m00 = r - l;
                Matrix_MVP.m03 = l;
                Matrix_MVP.m11 = t - b;
                Matrix_MVP.m13 = b;
                Matrix_MVP.m23 = -1;
                Matrix_MVP.m33 = 1;

                // 绘制贴图
                pageColorMat.SetVector("_SplatTileOffset", scaleOffset);
                pageColorMat.SetMatrix(Shader.PropertyToID("_Matrix_MVP"), GL.GetGPUProjectionMatrix(Matrix_MVP, true));

                int layerIndex = 0;

                foreach (var alphamap in Terrain.terrainData.alphamapTextures)
                {
                    pageColorMat.SetTexture("_SplatTexture", alphamap);

                    int index = 1;
                    for(;layerIndex < Terrain.terrainData.terrainLayers.Length && index <= 4;layerIndex ++)
                    {
                        var layer = Terrain.terrainData.terrainLayers[layerIndex];
                        var tileScale = new Vector2(Terrain.terrainData.size.x / layer.tileSize.x, Terrain.terrainData.size.z / layer.tileSize.y);
                        var tileOffset = new Vector4(tileScale.x * scaleOffset.x, tileScale.y * scaleOffset.y, scaleOffset.z * tileScale.x, scaleOffset.w * tileScale.y);
                        pageColorMat.SetVector("_SurfaceTileOffset", tileOffset);
                        pageColorMat.SetTexture($"_AlbedoTexture{index}", layer.diffuseTexture);
                        pageColorMat.SetTexture($"_NormalTexture{index}", layer.normalMapTexture);
                        index++;
                    }
                    CommandBuffer CmdBuffer = CommandBufferPool.Get("DrawPageColor");
                    CmdBuffer.SetRenderTarget(VirtualTextureProfile.colorBuffers, VirtualTextureProfile.colorBuffers[0]);
                    CmdBuffer.DrawMesh(quadMesh, Matrix4x4.identity, pageColorMat, 0, layerIndex <= 4 ? 0 : 1);
                    Graphics.ExecuteCommandBuffer(CmdBuffer);
                    CommandBufferPool.Release(CmdBuffer);
                }
            }
        }

        void OnDisable()
        {
            if (CheckRunSystem()) { return; }

            pageProducer.Dispose();
            pageRenderer.Dispose();
            feedbackRenderer.Dispose();
            VirtualTextureProfile.Dispose();
        }

        public void Reset()
        {
            if (CheckRunSystem()) { return; }

            pageProducer.Reset();
            VirtualTextureProfile.Reset();
        }

        private bool CheckRunSystem()
        {
            if (TerrainList.Length == 0 && PlayerCamera == null && FeedbackCamera == null && VirtualTextureProfile == null) { return true; }

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
        
    }
}