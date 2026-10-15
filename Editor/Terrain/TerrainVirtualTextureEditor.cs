using UnityEditor;
using UnityEngine;

namespace Landscape.RuntimeVirtualTexture.Editor
{
    /// <summary>在场景里画出体积范围，并让 Transform 的 XZ 缩放跟随体积尺寸。</summary>
    [CustomEditor(typeof(TerrainVirtualTexture))]
    public class TerrainVirtualTextureEditor : UnityEditor.Editor
    {
        static readonly Color s_BoundColor = new Color(0.5f, 1, 0.25f);

        TerrainVirtualTexture volume => (TerrainVirtualTexture)target;

        void OnSceneGUI()
        {
            Transform transform = volume.transform;
            Handles.color = s_BoundColor;
            Handles.DrawWireCube(transform.position, transform.localScale);
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            Vector3 scale = volume.transform.localScale;
            volume.transform.localScale = new Vector3(volume.size, scale.y, volume.size);
        }
    }
}
