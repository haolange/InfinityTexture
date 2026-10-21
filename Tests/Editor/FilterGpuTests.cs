using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace Landscape.RuntimeVirtualTexture.Tests
{
    internal class FilterGpuTests
    {
        [TestCase(0f, false)]
        [TestCase(2f, false)]
        [TestCase(3.5f, false)]
        [TestCase(1.25f, true)]
        [TestCase(1.25f, false)]
        [TestCase(-1f, false)]
        public void TrilinearMatchesIndependentTwoLevelSampling(float lod, bool ancestorOnly)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsAsyncGPUReadback) { Assert.Ignore("Requires GPU readback"); }
            var indirect = new Texture2D(8, 8, TextureFormat.RGBA32, true, true);
            var physical = new Texture2D(48, 48, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var material = new Material(Shader.Find("Hidden/VirtualTexture/Tests/Filter"));
            var target = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGBFloat);
            var cmd = new CommandBuffer();
            try
            {
                var pixels = new Color[48 * 48];
                for (int i = 0; i < pixels.Length; ++i) { pixels[i] = new Color((i * 17 % 97) / 96f, (i * 7 % 53) / 52f, (i * 29 % 31) / 30f, (i * 3 % 43) / 42f); }
                physical.SetPixels(pixels); physical.Apply(false);
                for (int mip = 0; mip < indirect.mipmapCount; ++mip)
                {
                    int size = 8 >> mip;
                    var entries = new Color32[size * size];
                    for (int y = 0; y < size; ++y)
                    for (int x = 0; x < size; ++x) { entries[y * size + x] = ancestorOnly ? new Color32(1, 1, 3, 255) : new Color32((byte)(x % 4), (byte)(y % 4), (byte)mip, (byte)(x == 0 && y == 0 ? 0 : 255)); }
                    indirect.SetPixels32(entries, mip);
                }
                indirect.Apply(false);
                material.SetTexture("_VTIndirectMap", indirect);
                material.SetTexture("_VTPhysicalBaseColor", physical); material.SetTexture("_VTPhysicalNormal", physical); material.SetTexture("_VTPhysicalMask", physical);
                material.SetVector("_VTParams", new Vector4(8, 4, 4, 4)); material.SetVector("_VTPoolParams", new Vector4(12, 1f / 48, 0, 0));
                material.SetInt("_VTLayerCount", 3); material.SetFloat("_TestLod", lod);
                target.Create();
                for (int layer = 0; layer < 3; ++layer)
                {
                    material.SetInt("_TestLayer", layer);
                    cmd.Clear(); cmd.SetRenderTarget(target); cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                    Graphics.ExecuteCommandBuffer(cmd);
                    var read = AsyncGPUReadback.Request(target, 0); read.WaitForCompletion(); Assert.IsFalse(read.hasError);
                    foreach (Color error in read.GetData<Color>()) { Assert.LessOrEqual(Mathf.Max(error.r, error.g, error.b, error.a), 1e-6f); }
                }
            }
            finally { cmd.Dispose(); target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(material); Object.DestroyImmediate(physical); Object.DestroyImmediate(indirect); }
        }
    }
}
