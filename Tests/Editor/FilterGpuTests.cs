using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace Landscape.RuntimeVirtualTexture.Tests
{
    internal class FilterGpuTests
    {
        [Test]
        public void TemporalMipChoosesWholeLevelsWithTheExpectedProbability()
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
                for (int y = 12; y < 24; ++y)
                for (int x = 12; x < 24; ++x) { pixels[y * 48 + x] = Color.white; }
                physical.SetPixels(pixels); physical.Apply(false);
                for (int mip = 0; mip < 4; ++mip)
                {
                    int size = 8 >> mip;
                    var entries = new Color32[size * size];
                    for (int i = 0; i < entries.Length; ++i) { entries[i] = mip == 0 ? new Color32(0, 0, 0, 255) : new Color32(1, 1, (byte)mip, 255); }
                    indirect.SetPixels32(entries, mip);
                }
                indirect.Apply(false); target.Create();
                Shader.SetGlobalTexture("_VTIndirectMap", indirect); Shader.SetGlobalTexture("_VTPhysicalBaseColor", physical);
                Shader.SetGlobalVector("_VTParams", new Vector4(8, 4, 4, 4)); Shader.SetGlobalVector("_VTPoolParams", new Vector4(12, 1f / 48, 0, 0));
                Shader.SetGlobalInteger("_VTLayerCount", 1); Shader.SetGlobalInteger("_VTTemporalEnabled", 1); Shader.SetGlobalInteger("_TestTemporal", 1); Shader.SetGlobalFloat("_TestLod", .25f);
                float sum = 0; int changed = 0; Color[] previous = null;
                for (int frame = 0; frame < 4; ++frame)
                {
                    Shader.SetGlobalInteger("_VTTemporalSeed", frame * 747796405);
                    cmd.Clear(); cmd.SetRenderTarget(target); cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3); Graphics.ExecuteCommandBuffer(cmd);
                    var read = AsyncGPUReadback.Request(target); read.WaitForCompletion(); Assert.IsFalse(read.hasError);
                    Color[] values = read.GetData<Color>().ToArray();
                    for (int i = 0; i < values.Length; ++i)
                    {
                        Assert.That(values[i].r, Is.EqualTo(0f).Within(1e-6f).Or.EqualTo(1f).Within(1e-6f));
                        Assert.AreEqual(1f, values[i].a);
                        sum += values[i].r;
                        if (previous != null && values[i].r != previous[i].r) { ++changed; }
                    }
                    previous = values;
                }
                Assert.AreEqual(.25f, sum / (4 * 64 * 64), .02f);
                Assert.Greater(changed, 2000, "Frame seed must change the selected mip");
            }
            finally { cmd.Dispose(); target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(material); Object.DestroyImmediate(physical); Object.DestroyImmediate(indirect); }
        }

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
                Shader.SetGlobalTexture("_VTIndirectMap", indirect);
                Shader.SetGlobalTexture("_VTPhysicalBaseColor", physical); Shader.SetGlobalTexture("_VTPhysicalNormal", physical); Shader.SetGlobalTexture("_VTPhysicalMask", physical);
                Shader.SetGlobalVector("_VTParams", new Vector4(8, 4, 4, 4)); Shader.SetGlobalVector("_VTPoolParams", new Vector4(12, 1f / 48, 0, 0));
                Shader.SetGlobalInteger("_TestTemporal", 0); Shader.SetGlobalInteger("_VTTemporalEnabled", 0); Shader.SetGlobalInteger("_VTNormalIsRG", 0); Shader.SetGlobalInteger("_VTLayerCount", 3); Shader.SetGlobalFloat("_TestLod", lod);
                target.Create();
                for (int layer = 0; layer < 3; ++layer)
                {
                    Shader.SetGlobalInteger("_TestLayer", layer);
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
