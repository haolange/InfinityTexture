using UnityEditor;
using UnityEngine;

namespace Landscape.RuntimeVirtualTexture.Editor
{
    [CustomEditor(typeof(VirtualTextureVolume))]
    public class VirtualTextureVolumeEditor : UnityEditor.Editor
    {
        public Bounds volumeBound;
        VirtualTextureVolume volumeTarget { get { return target as VirtualTextureVolume; } }


        void OnEnable()
        {

        }

        void OnValidate()
        {

        }

        void OnSceneGUI()
        {
            volumeBound = new Bounds(volumeTarget.transform.position, volumeTarget.transform.localScale);
            DrawBound(volumeBound, new Color(0.5f, 1, 0.25f));
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();
            serializedObject.ApplyModifiedProperties();

            volumeTarget.transform.localScale = new Vector3(volumeTarget.VolumeSize, volumeTarget.transform.localScale.y, volumeTarget.VolumeSize);
        }

        protected void DrawBound(Bounds b, Color DebugColor)
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

    }
}
