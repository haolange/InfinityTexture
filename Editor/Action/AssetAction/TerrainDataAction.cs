using UnityEditor;
using UnityEngine;
using Landscape.RuntimeVirtualTexture;
using UnityEditor.ProjectWindowCallback;

namespace Landscape.RuntimeVirtualTexture.Editor
{
    internal class TerrainDataWizard : ScriptableWizard
    {
        private TerrainData m_TerrainData;


        void OnEnable()
        {

        }

        void OnWizardCreate()
        {
            for(int i = 0; i < m_TerrainData.alphamapTextureCount; ++i)
            {
                Texture2D splatTexture = m_TerrainData.GetAlphamapTexture(i);
                splatTexture.minimumMipmapLevel = 1;
                splatTexture.Apply();
            }
        }

        void OnWizardOtherButton()
        {

        }

        void OnWizardUpdate()
        {
            
        }

        public void SetTerrainData(TerrainData terrainData)
        {
            this.m_TerrainData = terrainData;
        }
    }

    public static class TerrainDataAction
    {
        [MenuItem("Assets/AssetActions/Landscape/TerrainData/SplatTextureEdit", priority = 32)]
        public static void SplatTextureEdit(MenuCommand menuCommand)
        {
            Object activeObject = Selection.activeObject;
            if (activeObject.GetType() != typeof(TerrainData))
            {
                Debug.LogWarning("select asset is not TerrainData");
                return;
            }

            TerrainDataWizard meshAssetWizard = ScriptableWizard.DisplayWizard<TerrainDataWizard>("Setting", "Applay");
            meshAssetWizard.SetTerrainData((TerrainData)activeObject);
        }
    }
}
