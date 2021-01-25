using UnityEngine;

namespace Landscape.ProceduralVirtualTexture
{
    public enum EVirtualTextureVolumeSize
    {
        X128 = 128,
        X256 = 256,
        X512 = 512,
        X1024 = 1024,
        X2048 = 2048,
    }

    [ExecuteInEditMode]
    public class RuntimeVirtualTextureVolume : MonoBehaviour
    {
        [HideInInspector]
        public Bounds BoundBox;

        [HideInInspector]
        public RuntimeVirtualTextureSystem VirtualTextureSystem;

        public EVirtualTextureVolumeSize VolumeScale = EVirtualTextureVolumeSize.X1024;

        private float VolumeSize
        {
            get
            {
                return (int)VolumeScale;
            }
        }

        //public VirtualTextureProfile VirtualTextureProfile;


        public RuntimeVirtualTextureVolume()
        {

        }

        void OnEnable()
        {
            //VirtualTextureProfile.Initialize();
            Shader.SetGlobalVector("_VTVolumeInfo", new Vector4(transform.position.x, transform.position.y, transform.position.z, VolumeSize));
            VirtualTextureSystem = GetComponent<RuntimeVirtualTextureSystem>();
        }

        void Update()
        {
            transform.localScale = new Vector3(VolumeSize, transform.localScale.y, VolumeSize);

            if(VirtualTextureSystem)
            {
                VirtualTextureSystem.VolumeSize = VolumeSize;
            }
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

        private void DrawBound()
        {
            BoundBox = new Bounds(transform.position, transform.localScale);
            DrawBound(BoundBox, new Color(0.5f, 1, 0.25f));
        }

        void OnDrawGizmosSelected()
        {
            DrawBound();
        }
#endif
        /*void OnDisable()
        {
            VirtualTextureProfile.Release();
        }*/
    }
}
