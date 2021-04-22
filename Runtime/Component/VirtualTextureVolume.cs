using UnityEngine;

namespace Landscape.RuntimeVirtualTexture
{
    public enum EVirtualTextureVolumeSize
    {
        X128 = 128,
        X256 = 256,
        X512 = 512,
        X1024 = 1024,
        X2048 = 2048,
        X4096 = 4096,
    }

    public class VirtualTextureVolume : MonoBehaviour
    {
        public VirtualTextureAsset virtualTexture;
        public EVirtualTextureVolumeSize volumeSize = EVirtualTextureVolumeSize.X1024;

        [HideInInspector]
        public Bounds boundBox;
        [HideInInspector]
        public Material material;
        private float VolumeSize
        {
            get
            {
                return (int)volumeSize;
            }
        }
        
        internal FPageProducer pageProducer;
        internal FPageRenderer pageRenderer;
        internal static VirtualTextureVolume s_VirtualTextureVolume;


        void OnEnable()
        {
            virtualTexture?.Initialize();
            pageRenderer = new FPageRenderer(virtualTexture.pageSize);
            pageProducer = new FPageProducer(virtualTexture.pageSize, virtualTexture.MaxMipLevel);

            s_VirtualTextureVolume = this;

            Terrain[] terrainList = Object.FindObjectsOfType<Terrain>();
            foreach(Terrain terrain in terrainList)
            {
                terrain.materialTemplate = material;
            }
        }

        void Update()
        {
            transform.localScale = new Vector3(VolumeSize, transform.localScale.y, VolumeSize);
        }

        void OnDisable()
        {
            pageProducer.Dispose();
            pageRenderer.Dispose();
            virtualTexture?.Release();
        }

#if UNITY_EDITOR
        protected static void DrawBound(Bounds b, Color DebugColor)
        {
            // bottom
            var p1 = new Vector3(b.min.x, b.min.y, b.min.z);
            var p2 = new Vector3(b.max.x, b.min.y, b.min.z);
            var p3 = new Vector3(b.max.x, b.min.y, b.max.z);
            var p4 = new Vector3(b.min.x, b.min.y, b.max.z);

            Debug.DrawLine(p1, p2, DebugColor);
            Debug.DrawLine(p2, p3, DebugColor);
            Debug.DrawLine(p3, p4, DebugColor);
            Debug.DrawLine(p4, p1, DebugColor);

            // top
            var p5 = new Vector3(b.min.x, b.max.y, b.min.z);
            var p6 = new Vector3(b.max.x, b.max.y, b.min.z);
            var p7 = new Vector3(b.max.x, b.max.y, b.max.z);
            var p8 = new Vector3(b.min.x, b.max.y, b.max.z);

            Debug.DrawLine(p5, p6, DebugColor);
            Debug.DrawLine(p6, p7, DebugColor);
            Debug.DrawLine(p7, p8, DebugColor);
            Debug.DrawLine(p8, p5, DebugColor);

            // sides
            Debug.DrawLine(p1, p5, DebugColor);
            Debug.DrawLine(p2, p6, DebugColor);
            Debug.DrawLine(p3, p7, DebugColor);
            Debug.DrawLine(p4, p8, DebugColor);
        }

        void OnDrawGizmosSelected()
        {
            boundBox = new Bounds(transform.position, transform.localScale);
            DrawBound(boundBox, new Color(0.5f, 1, 0.25f));
        }
#endif
    }
}
