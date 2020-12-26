using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace Landscape.ProceduralVirtualTexture
{
    [CreateAssetMenu(menuName = "Landscape/RuntimeVirtualTexture")]
    public class RuntimeVirtualTexture : ScriptableObject
    {
        [Range(4, 32)]
        public int TileNum = 10;

        [Range(64, 1024)]
        public int TileSize = 256;

        [Range(1, 4)]
        public int TileBorder = 4;

        //[HideInInspector]
        [Range(256, 1024)]
        public int PageSize = 256;

        public int MaxMipLevel { get { return (int)Mathf.Log(PageSize, 2); } }

        [HideInInspector]
        public int TileSizePadding { get { return TileSize + TileBorder * 2; } }

        [HideInInspector]
        public int TextureSize { get { return TileNum * TileSizePadding; } }


        [HideInInspector]
        public RenderTexture PhyscisTextureA;

        [HideInInspector]
        public RenderTexture PhyscisTextureB;

        [HideInInspector]
        public RenderTexture PageTableTexture;


        [HideInInspector]
        public RenderTargetIdentifier DepthBuffer;
        public RenderTargetIdentifier[] ColorBuffer;

        private PagePool PagePool;


        public RuntimeVirtualTexture()
        {

        }

        public void Initialize()
        {
            PagePool = new PagePool();
            PagePool.Init(TileNum * TileNum);

            RenderTextureDescriptor TextureADesc = new RenderTextureDescriptor { width = TextureSize, height = TextureSize, volumeDepth = 1, dimension = TextureDimension.Tex2D, graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = 0, mipCount = -1, useMipMap = false, autoGenerateMips = false, bindMS = false, msaaSamples = 1 };
            PhyscisTextureA = new RenderTexture(TextureADesc);
            PhyscisTextureA.name = "PhyscisTextureA";
            PhyscisTextureA.filterMode = FilterMode.Bilinear;
            PhyscisTextureA.wrapMode = TextureWrapMode.Clamp;

            RenderTextureDescriptor TextureBDesc = new RenderTextureDescriptor { width = TextureSize, height = TextureSize, volumeDepth = 1, dimension = TextureDimension.Tex2D, graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = 0, mipCount = -1, useMipMap = false, autoGenerateMips = false, bindMS = false, msaaSamples = 1 };
            PhyscisTextureB = new RenderTexture(TextureBDesc);
            PhyscisTextureB.name = "PhyscisTextureB";
            PhyscisTextureB.filterMode = FilterMode.Bilinear;
            PhyscisTextureB.wrapMode = TextureWrapMode.Clamp;

            PageTableTexture = new RenderTexture(PageSize, PageSize, 0, GraphicsFormat.R8G8B8A8_UNorm);
            PageTableTexture.name = "PageTableTexture";
            PageTableTexture.filterMode = FilterMode.Point;
            PageTableTexture.wrapMode = TextureWrapMode.Clamp;

            DepthBuffer = new RenderTargetIdentifier(PhyscisTextureA);
            ColorBuffer = new RenderTargetIdentifier[2];
            ColorBuffer[0] = new RenderTargetIdentifier(PhyscisTextureA);
            ColorBuffer[1] = new RenderTargetIdentifier(PhyscisTextureB);

            // 设置Shader参数
            // x: padding偏移量
            // y: tile有效区域的尺寸
            // zw: 1/区域尺寸
            Shader.SetGlobalTexture("_PhyscisAlbedo", PhyscisTextureA);
            Shader.SetGlobalTexture("_PhyscisNormal", PhyscisTextureB);
            Shader.SetGlobalVector("_VTPageTileParams", new Vector4((float)TileBorder, (float)TileSize, TextureSize, TextureSize));

            Shader.SetGlobalTexture("_PageTableTexture", PageTableTexture);
            Shader.SetGlobalVector("_VTPageTableParams", new Vector4(PageSize, 1 / PageSize, MaxMipLevel, 0));
        }

        public void Reset()
        {
            Release();
            Initialize();
        }

        public void Release()
        {
            PhyscisTextureA.Release();
            PhyscisTextureB.Release();
            PageTableTexture.Release();
        }

        public Vector2Int RequestTile()
        {
            return IdToPos(PagePool.First);
        }

        public bool SetActive(in Vector2Int tile)
        {
            bool success = PagePool.SetActive(PosToId(tile));

            return success;
        }

        private Vector2Int IdToPos(in int id)
        {
            return new Vector2Int(id % TileNum, id / TileNum);
        }

        private int PosToId(in Vector2Int tile)
        {
            return (tile.y * TileNum + tile.x);
        }
    }
}