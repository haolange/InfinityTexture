using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace Landscape.RuntimeVirtualTexture
{
    [CreateAssetMenu(menuName = "Landscape/VirtualTextureAsset", order = 256)]
    public class VirtualTextureAsset : ScriptableObject
    {
        [Header("Size")]
        [Range(8, 16)]
        public int tileNum = 16;
        [Range(64, 2048)]
        public int tileSize = 256;
        [Range(256, 2048)]
        public int pageSize = 256;

        [Header("Layout")]
        public bool enableCompression = true;

        public int MaxMipLevel { get { return (int)math.log2(pageSize) + 1; } }
        public int QuadTileSize { get { return tileSize / 4; } }

        [HideInInspector]
        public int LastChangeHash;
        public int ChangeHash { get { return new int3(tileNum, tileSize, pageSize).GetHashCode(); } }

        [HideInInspector]
        public RenderTexture renderTextureA;
        [HideInInspector]
        public RenderTexture renderTextureB;
        [HideInInspector]
        public RenderTexture compressTextureA;
        [HideInInspector]
        public RenderTexture compressTextureB;
        [HideInInspector]
        public Texture2DArray physcisTextureA;
        [HideInInspector]
        public Texture2DArray physcisTextureB;
        [HideInInspector]
        public RenderTexture pageTableTexture;
        [HideInInspector]
        public RenderTargetIdentifier depthBuffer;
        [HideInInspector]
        public RenderTargetIdentifier[] colorBuffer;

        [HideInInspector]
        internal FLruCache lruCache;


        public VirtualTextureAsset()
        {

        }

        void Reset()
        {
            //Debug.Log("Reset");
            //BuildVTAsset();
        }

        void OnEnable()
        {
            //Debug.Log("OnEnable");
            //Initialize();
        }

        void OnValidate()
        {
            //Debug.Log("OnValidate");
            //BuildVTAsset();
        }

        void OnDisable()
        {
            //Debug.Log("OnDisable");
            //Release();
        }

        void OnDestroy()
        {
            //Debug.Log("OnDestroy");
            //Release();
        }

        public void Initialize()
        {
            lruCache = new FLruCache(tileNum * tileNum);

            GraphicsFormat graphicsFormat;
    #if UNITY_ANDROID && !UNITY_EDITOR
            graphicsFormat = GraphicsFormat.RGBA_ETC2_UNorm;
    #else
            graphicsFormat = GraphicsFormat.RGBA_DXT5_UNorm;
#endif

            RenderTextureDescriptor TextureADesc = new RenderTextureDescriptor { width = tileSize, height = tileSize, volumeDepth = 1, dimension = TextureDimension.Tex2D, graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = 0, mipCount = -1, useMipMap = false, autoGenerateMips = false, bindMS = false, msaaSamples = 1 };
            renderTextureA = new RenderTexture(TextureADesc);
            renderTextureA.name = "CopyTextureA";
            renderTextureA.filterMode = FilterMode.Bilinear;
            renderTextureA.wrapMode = TextureWrapMode.Clamp;

            RenderTextureDescriptor TextureBDesc = new RenderTextureDescriptor { width = tileSize, height = tileSize, volumeDepth = 1, dimension = TextureDimension.Tex2D, graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = 0, mipCount = -1, useMipMap = false, autoGenerateMips = false, bindMS = false, msaaSamples = 1 };
            renderTextureB = new RenderTexture(TextureBDesc);
            renderTextureB.name = "CopyTextureB";
            renderTextureB.filterMode = FilterMode.Bilinear;
            renderTextureB.wrapMode = TextureWrapMode.Clamp;

            compressTextureA = new RenderTexture(QuadTileSize, QuadTileSize, 0)
            {
                graphicsFormat = GraphicsFormat.R32G32B32A32_UInt,
                enableRandomWrite = true,
            };
            compressTextureA.Create();

            compressTextureB = new RenderTexture(QuadTileSize, QuadTileSize, 0)
            {
                graphicsFormat = GraphicsFormat.R32G32B32A32_UInt,
                enableRandomWrite = true,
            };
            compressTextureB.Create();

            physcisTextureA = new Texture2DArray(tileSize, tileSize, tileNum, graphicsFormat, TextureCreationFlags.None);
            physcisTextureA.name = "PhyscisTextureA";

            physcisTextureB = new Texture2DArray(tileSize, tileSize, tileNum, graphicsFormat, TextureCreationFlags.None);
            physcisTextureB.name = "PhyscisTextureB";

            pageTableTexture = new RenderTexture(pageSize, pageSize, 0, GraphicsFormat.R8G8_UInt);
            pageTableTexture.name = "PageTableTexture";
            pageTableTexture.filterMode = FilterMode.Point;
            pageTableTexture.wrapMode = TextureWrapMode.Clamp;

            colorBuffer = new RenderTargetIdentifier[2];
            depthBuffer = default;
            colorBuffer[0] = new RenderTargetIdentifier(renderTextureA);
            colorBuffer[1] = new RenderTargetIdentifier(renderTextureB);
        }

        public void Release()
        {
            if(physcisTextureA != null && physcisTextureB != null && pageTableTexture != null)
            {
                lruCache.Dispose();
                pageTableTexture.Release();
                Object.DestroyImmediate(physcisTextureA);
                Object.DestroyImmediate(physcisTextureB);
                Object.DestroyImmediate(pageTableTexture);
            }
        }
    }
}
