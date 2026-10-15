using System;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Experimental.Rendering;

namespace Landscape.RuntimeVirtualTexture
{
    public enum EBorder
    {
        X0 = 0,
        X1 = 1,
        X2 = 2,
        X4 = 4
    }

    public enum ECompressMode
    {
        None = 0,
        Bit8 = 1,
        Bit16 = 2
    }

    [CreateAssetMenu(menuName = "Landscape/VirtualTextureAsset")]
    public unsafe class VirtualTextureAsset : ScriptableObject, IDisposable
    {
        [Range(8, 32)]
        public int tileNum = 16;
        [Range(64, 512)]
        public int tileSize = 256;
        public EBorder tileBorder = EBorder.X4;
        [Range(128, 4096)]
        public int pageSize = 256;
        public ECompressMode compressMode = ECompressMode.Bit16;

        [Header("Request")]
        [Tooltip("每轮反馈最多烘焙的页数")]
        [Range(1, 64)]
        public int bakesPerRound = 8;
        [Tooltip("排队页的上限")]
        [Range(16, 4096)]
        public int queueCapacity = 512;
        [Tooltip("排队页连续多少轮没被请求就退回空闲")]
        [Range(1, 600)]
        public int queueTimeout = 30;

        public int NumMip { get { return (int)math.log2(pageSize) + 1; } }
        public int TileSizePadding { get { return tileSize + (int)tileBorder * 2; } }
        public int QuadTileSizePadding { get { return TileSizePadding / 4; } }

        [HideInInspector]
        public ComputeShader m_Shader;

        internal RenderTargetIdentifier[] colorTextureIDs;
        internal RTHandle[] colorTextureHandles;
        internal int TextureSize { get { return tileNum * TileSizePadding; } }

        private RenderTexture m_physicsTextureA;
        private RenderTexture m_physicsTextureB;

        public void Initialize()
        {
            RenderTextureFormat format = RenderTextureFormat.ARGB32;
            switch (compressMode)
            {
                case ECompressMode.Bit8:
                    format = RenderTextureFormat.RGB565;
                    break;
                case ECompressMode.Bit16:
                    format = RenderTextureFormat.RGB565;
                    break;
            }
            
            RenderTextureDescriptor textureDesctiptor = new RenderTextureDescriptor { width = TextureSize, height = TextureSize, volumeDepth = 1, dimension = TextureDimension.Tex2D, colorFormat = format, depthBufferBits = 0, mipCount = -1, useMipMap = false, autoGenerateMips = false, bindMS = false, msaaSamples = 1 };

            //physics texture
            m_physicsTextureA = new RenderTexture(textureDesctiptor);
            m_physicsTextureA.bindTextureMS = false;
            m_physicsTextureA.name = "PhysicsTextureA";
            m_physicsTextureA.wrapMode = TextureWrapMode.Clamp;
            m_physicsTextureA.filterMode = FilterMode.Trilinear;
            m_physicsTextureA.anisoLevel = 16;
            m_physicsTextureA.Create();

            m_physicsTextureB = new RenderTexture(textureDesctiptor);
            m_physicsTextureB.bindTextureMS = false;
            m_physicsTextureB.name = "PhysicsTextureB";
            m_physicsTextureB.wrapMode = TextureWrapMode.Clamp;
            m_physicsTextureB.filterMode = FilterMode.Trilinear;
            m_physicsTextureB.anisoLevel = 16;
            m_physicsTextureB.Create();

            colorTextureIDs = new RenderTargetIdentifier[2];
            colorTextureIDs[0] = new RenderTargetIdentifier(m_physicsTextureA);
            colorTextureIDs[1] = new RenderTargetIdentifier(m_physicsTextureB);
            colorTextureHandles = new[] { RTHandles.Alloc(m_physicsTextureA), RTHandles.Alloc(m_physicsTextureB) };

            Shader.SetGlobalTexture("_PhyscisAlbedo", m_physicsTextureA);
            Shader.SetGlobalTexture("_PhyscisNormal", m_physicsTextureB);
            Shader.SetGlobalVector("_VTParams", new Vector4(pageSize, NumMip, tileSize, (int)tileBorder));
            Shader.SetGlobalVector("_VTPoolParams", new Vector4(TileSizePadding, 1.0f / TextureSize, 0, 0));
        }

        public void Dispose()
        {
            colorTextureHandles[0].Release();
            colorTextureHandles[1].Release();

            m_physicsTextureA.Release();
            m_physicsTextureB.Release();
            Object.Destroy(m_physicsTextureA);
            Object.Destroy(m_physicsTextureB);
        }
    }
}
