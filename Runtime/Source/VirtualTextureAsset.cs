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

        public int NumMip { get { return (int)math.log2(pageSize) + 1; } }
        public int TileSizePadding { get { return tileSize + (int)tileBorder * 2; } }
        public int QuadTileSizePadding { get { return TileSizePadding / 4; } }

        [HideInInspector]
        public ComputeShader m_Shader;

        internal FLruCache* lruCache;
        internal RenderTargetIdentifier tableTextureID;
        internal RenderTargetIdentifier[] colorTextureIDs;
        internal int TextureSize { get { return tileNum * TileSizePadding; } }

        private RenderTexture m_physicsTextureA;
        private RenderTexture m_physicsTextureB;
        private RenderTexture m_pageTableTexture;

        public void Initialize()
        {
            lruCache = (FLruCache*)UnsafeUtility.Malloc(Marshal.SizeOf(typeof(FLruCache)) * 1, 64, Allocator.Persistent);
            FLruCache.BuildLruCache(ref lruCache[0], tileNum * tileNum);

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
            m_physicsTextureA.filterMode = FilterMode.Bilinear;

            m_physicsTextureB = new RenderTexture(textureDesctiptor);
            m_physicsTextureB.bindTextureMS = false;
            m_physicsTextureB.name = "PhysicsTextureB";
            m_physicsTextureB.wrapMode = TextureWrapMode.Clamp;
            m_physicsTextureB.filterMode = FilterMode.Bilinear;

            colorTextureIDs = new RenderTargetIdentifier[2];
            colorTextureIDs[0] = new RenderTargetIdentifier(m_physicsTextureA);
            colorTextureIDs[1] = new RenderTargetIdentifier(m_physicsTextureB);

            m_pageTableTexture = new RenderTexture(pageSize, pageSize, 0, GraphicsFormat.R8G8B8A8_UNorm);
            m_pageTableTexture.bindTextureMS = false;
            m_pageTableTexture.name = "PageTableTexture";
            m_pageTableTexture.filterMode = FilterMode.Point;
            m_pageTableTexture.wrapMode = TextureWrapMode.Clamp;

            tableTextureID = new RenderTargetIdentifier(m_pageTableTexture);

            // 设置Shader参数
            // x: padding 偏移量
            // y: tile 有效区域的尺寸
            // zw: 1/区域尺寸
            Shader.SetGlobalTexture("_PhyscisAlbedo", m_physicsTextureA);
            Shader.SetGlobalTexture("_PhyscisNormal", m_physicsTextureB);
            Shader.SetGlobalTexture("_PageTableTexture", m_pageTableTexture);
            Shader.SetGlobalVector("_VTPageParams", new Vector4(pageSize, 1 / pageSize, NumMip - 1, 0));
            Shader.SetGlobalVector("_VTPageTileParams", new Vector4((float)tileBorder, (float)tileSize, TextureSize, TextureSize));
        }

        public void Dispose()
        {
            lruCache[0].Dispose();
            UnsafeUtility.Free((void*)lruCache, Allocator.Persistent);

            m_physicsTextureA.Release();
            m_physicsTextureB.Release();
            m_pageTableTexture.Release();
            Object.DestroyImmediate(m_physicsTextureA);
            Object.DestroyImmediate(m_physicsTextureB);
            Object.DestroyImmediate(m_pageTableTexture);
        }
    }
}