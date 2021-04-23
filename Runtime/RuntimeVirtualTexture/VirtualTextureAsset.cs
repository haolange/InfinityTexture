using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Unity.Mathematics;

namespace Landscape.RuntimeVirtualTexture
{
    [CreateAssetMenu(menuName = "Landscape/VirtualTextureAsset")]
    public class VirtualTextureAsset : ScriptableObject
    {
        [Range(4, 32)]
        public int tileNum = 16;
        [Range(64, 1024)]
        public int tileSize = 256;
        [Range(0, 4)]
        public int tileBorder = 4;
        [Range(256, 1024)]
        public int pageSize = 256;

        public int MaxMip { get { return (int)math.log2(pageSize) + 1; } }
        public int TileSizePadding { get { return tileSize + tileBorder * 2; } }

        internal FLruCache lruCache;
        internal RenderTexture physcisTextureA;
        internal RenderTexture physcisTextureB;
        internal RenderTexture pageTableTexture;
        internal RenderTargetIdentifier[] colorBuffers;
        internal int TextureSize { get { return tileNum * TileSizePadding; } }

        public VirtualTextureAsset()
        {

        }

        public void Initialize()
        {
            lruCache = new FLruCache(tileNum * tileNum);

            RenderTextureDescriptor TextureADesc = new RenderTextureDescriptor { width = TextureSize, height = TextureSize, volumeDepth = 1, dimension = TextureDimension.Tex2D, graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = 0, mipCount = -1, useMipMap = false, autoGenerateMips = false, bindMS = false, msaaSamples = 1 };
            physcisTextureA = new RenderTexture(TextureADesc);
            physcisTextureA.name = "PhyscisTextureA";
            physcisTextureA.filterMode = FilterMode.Bilinear;
            physcisTextureA.wrapMode = TextureWrapMode.Clamp;
            physcisTextureA.anisoLevel = 8;

            RenderTextureDescriptor TextureBDesc = new RenderTextureDescriptor { width = TextureSize, height = TextureSize, volumeDepth = 1, dimension = TextureDimension.Tex2D, graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = 0, mipCount = -1, useMipMap = false, autoGenerateMips = false, bindMS = false, msaaSamples = 1 };
            physcisTextureB = new RenderTexture(TextureBDesc);
            physcisTextureB.name = "PhyscisTextureB";
            physcisTextureB.filterMode = FilterMode.Bilinear;
            physcisTextureB.wrapMode = TextureWrapMode.Clamp;
            physcisTextureB.anisoLevel = 8;

            pageTableTexture = new RenderTexture(pageSize, pageSize, 0, GraphicsFormat.R8G8B8A8_UNorm);
            pageTableTexture.name = "PageTableTexture";
            pageTableTexture.filterMode = FilterMode.Point;
            pageTableTexture.wrapMode = TextureWrapMode.Clamp;

            colorBuffers = new RenderTargetIdentifier[2];
            colorBuffers[0] = new RenderTargetIdentifier(physcisTextureA);
            colorBuffers[1] = new RenderTargetIdentifier(physcisTextureB);

            // 设置Shader参数
            // x: padding 偏移量
            // y: tile 有效区域的尺寸
            // zw: 1/区域尺寸
            Shader.SetGlobalTexture("_PhyscisAlbedo", physcisTextureA);
            Shader.SetGlobalTexture("_PhyscisNormal", physcisTextureB);
            Shader.SetGlobalVector("_VTPageTileParams", new Vector4((float)tileBorder, (float)tileSize, TextureSize, TextureSize));

            Shader.SetGlobalTexture("_PageTableTexture", pageTableTexture);
            Shader.SetGlobalVector("_VTPageTableParams", new Vector4(pageSize, 1 / pageSize, MaxMip - 1, 0));
        }

        public void Reset()
        {
            Release();
            Initialize();
        }

        public void Release()
        {
            lruCache.Dispose();

            physcisTextureA.Release();
            physcisTextureB.Release();
            pageTableTexture.Release();

            Object.DestroyImmediate(physcisTextureA);
            Object.DestroyImmediate(physcisTextureB);
            Object.DestroyImmediate(pageTableTexture);
        }

        public int2 RequestTile()
        {
            return new int2(lruCache.First % tileNum, lruCache.First / tileNum);
        }

        public bool SetActive(in int index)
        {
            return lruCache.SetActive(index);
        }
    }
}