using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace Landscape.ProceduralVirtualTexture
{
    [RequireComponent(typeof(RuntimeVirtualTextureVolume))]
    internal class RuntimeVirtualTextureSystem : MonoBehaviour
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

        private float PageSize
        {
            get
            {
                return VolumeSize / VirtualTextureProfile.PageSize;
            }
        }

        [Header("Terrain")]
        public Terrain[] TerrainList;
        public Material TerrainMaterial;

        [Header("FeedbackSetting")]
        public int2 FeedbackSize = new int2(1920, 1080);
        public FeedbackScale FeedbackScale = FeedbackScale.X16;

        [Header("VirtualTexture")]
        public Camera PlayerCamera;
        public Camera FeedbackCamera;
        public RuntimeVirtualTexture VirtualTextureProfile;


        private Mesh DrawPageMesh;
        private Rect VTVolumeParams;
        private Material DrawPageColorMaterial;


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

            Vector2Int fixedCenter = GetFixedCenter(GetFixedPos(transform.position));
            VTVolumeParams = new Rect(fixedCenter.x - VolumeRadius, fixedCenter.y - VolumeRadius, VolumeSize, VolumeSize);
            Shader.SetGlobalVector("_VTVolumeParams", new Vector4(VTVolumeParams.xMin, VTVolumeParams.yMin, VTVolumeParams.width, VTVolumeParams.height));

            BuildQuadMesh();
            DrawPageColorMaterial = new Material(Shader.Find("VirtualTexture/DrawPageColor"));

            pageProducer = new FPageProducer();
            pageRenderer = new FPageRenderer();
            feedbackReader = new FeedbackReader();
            feedbackRenderer = new FeedbackRenderer();

            VirtualTextureProfile.Initialize();
            pageProducer.Initialize(DrawPageMesh, pageRenderer, VirtualTextureProfile);
            feedbackRenderer.Initialize(PlayerCamera, FeedbackCamera, FeedbackSize, FeedbackScale, pageProducer, VirtualTextureProfile);
        }

        private void Update()
        {
            if (CheckRunSystem()) { return; }

            feedbackReader.ProcessFeedback(pageProducer);

            if (feedbackReader.bReady)
            {
                feedbackRenderer.FeedbackCamera.Render();
                feedbackReader.RequestReadback(feedbackRenderer.TargetTexture);
            }

            pageRenderer.DrawPageColor(this, pageProducer, ref VirtualTextureProfile.PagePool, VirtualTextureProfile.TileNum, VirtualTextureProfile.TileSizePadding);
        }

        public void DrawMesh(RectInt DrawPageRect, FPageRequestInfo DrawRequestInfo)
        {
            int x = DrawRequestInfo.pageX;
            int y = DrawRequestInfo.pageY;
            int perSize = (int)Mathf.Pow(2, DrawRequestInfo.mipLevel);
            x = x - x % perSize;
            y = y - y % perSize;

            var tableSize = VirtualTextureProfile.PageSize;

            var paddingEffect = VirtualTextureProfile.TileBorder * perSize * (VTVolumeParams.width / tableSize) / VirtualTextureProfile.TileSize;

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

                var scaleFactor = DrawPageRect.width / realRect.width;

                var position = new Rect(DrawPageRect.x + (needDrawRect.xMin - realRect.xMin) * scaleFactor,
                                        DrawPageRect.y + (needDrawRect.yMin - realRect.yMin) * scaleFactor,
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
                DrawPageColorMaterial.SetVector("_SplatTileOffset", scaleOffset);
                DrawPageColorMaterial.SetMatrix(Shader.PropertyToID("_Matrix_MVP"), GL.GetGPUProjectionMatrix(Matrix_MVP, true));

                int layerIndex = 0;

                foreach (var alphamap in Terrain.terrainData.alphamapTextures)
                {
                    DrawPageColorMaterial.SetTexture("_SplatTexture", alphamap);

                    int index = 1;
                    for(;layerIndex < Terrain.terrainData.terrainLayers.Length && index <= 4;layerIndex ++)
                    {
                        var layer = Terrain.terrainData.terrainLayers[layerIndex];
                        var tileScale = new Vector2(Terrain.terrainData.size.x / layer.tileSize.x, Terrain.terrainData.size.z / layer.tileSize.y);
                        var tileOffset = new Vector4(tileScale.x * scaleOffset.x, tileScale.y * scaleOffset.y, scaleOffset.z * tileScale.x, scaleOffset.w * tileScale.y);
                        DrawPageColorMaterial.SetVector("_SurfaceTileOffset", tileOffset);
                        DrawPageColorMaterial.SetTexture($"_AlbedoTexture{index}", layer.diffuseTexture);
                        DrawPageColorMaterial.SetTexture($"_NormalTexture{index}", layer.normalMapTexture);
                        index++;
                    }
                    CommandBuffer CmdBuffer = CommandBufferPool.Get("DrawPageColor");
                    CmdBuffer.SetRenderTarget(VirtualTextureProfile.ColorBuffer, VirtualTextureProfile.DepthBuffer);
                    CmdBuffer.DrawMesh(DrawPageMesh, Matrix4x4.identity, DrawPageColorMaterial, 0, layerIndex <= 4 ? 0 : 1);
                    Graphics.ExecuteCommandBuffer(CmdBuffer);
                    CommandBufferPool.Release(CmdBuffer);
                }
            }
        }

        void OnDisable()
        {
            if (CheckRunSystem()) { return; }

            pageProducer.Dispose();
            VirtualTextureProfile.Release();
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

        private Vector2Int GetFixedCenter(Vector2Int pos)
        {
            return new Vector2Int((int)Mathf.Floor(pos.x / VolumeRadius + 0.5f) * (int)VolumeRadius,
                                  (int)Mathf.Floor(pos.y / VolumeRadius + 0.5f) * (int)VolumeRadius);
        }

        private Vector2Int GetFixedPos(Vector3 pos)
        {
            return new Vector2Int((int)Mathf.Floor(pos.x / PageSize + 0.5f) * (int)PageSize,
                                  (int)Mathf.Floor(pos.z / PageSize + 0.5f) * (int)PageSize);
        }
        
        private void BuildQuadMesh()
        {
            List<Vector3> VertexArray = new List<Vector3>();
            List<int> IndexArray = new List<int>();
            List<Vector2> UB0Array = new List<Vector2>();

            VertexArray.Add(new Vector3(0, 1, 0.1f));
            VertexArray.Add(new Vector3(0, 0, 0.1f));
            VertexArray.Add(new Vector3(1, 0, 0.1f));
            VertexArray.Add(new Vector3(1, 1, 0.1f));

            UB0Array.Add(new Vector2(0, 1));
            UB0Array.Add(new Vector2(0, 0));
            UB0Array.Add(new Vector2(1, 0));
            UB0Array.Add(new Vector2(1, 1));

            IndexArray.Add(0);
            IndexArray.Add(1);
            IndexArray.Add(2);
            IndexArray.Add(2);
            IndexArray.Add(3);
            IndexArray.Add(0);

            DrawPageMesh = new Mesh();
            DrawPageMesh.SetVertices(VertexArray);
            DrawPageMesh.SetUVs(0, UB0Array);
            DrawPageMesh.SetTriangles(IndexArray, 0);
        }
    }

}