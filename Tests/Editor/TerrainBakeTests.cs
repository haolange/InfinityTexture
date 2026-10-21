using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Landscape.RuntimeVirtualTexture.Tests
{
    [TestFixture]
    internal class TerrainBakeTests
    {
        readonly List<Object> m_Owned = new List<Object>();
        TerrainPageProducer m_Producer;

        T Own<T>(T value) where T : Object
        {
            value.hideFlags = HideFlags.HideAndDontSave;
            m_Owned.Add(value);
            return value;
        }

        [SetUp]
        public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsAsyncGPUReadback ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) || !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
            {
                Assert.Ignore("Bake sampling tests require a graphics device, float render targets and GPU readback");
            }
        }

        [TearDown]
        public void TearDown()
        {
            m_Producer?.Dispose();
            m_Producer = null;
            for (int i = m_Owned.Count - 1; i >= 0; --i)
            {
                if (m_Owned[i] != null) { Object.DestroyImmediate(m_Owned[i]); }
            }
            m_Owned.Clear();
        }

        Texture2D Source(bool mipColors)
        {
            var texture = Own(new Texture2D(64, 64, TextureFormat.RGBAFloat, true, true));
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            if (mipColors)
            {
                for (int mip = 0; mip < texture.mipmapCount; ++mip)
                {
                    int size = math.max(1, 64 >> mip);
                    var pixels = new Color[size * size];
                    for (int i = 0; i < pixels.Length; ++i) { pixels[i] = new Color((mip + 1) / 8f, .25f, .5f, 1); }
                    texture.SetPixels(pixels, mip);
                }
                texture.Apply(false);
            }
            else
            {
                var pixels = new Color[64 * 64];
                for (int y = 0; y < 64; ++y)
                {
                    for (int x = 0; x < 64; ++x)
                    {
                        float value = .15f + .7f * ((x * 17 + y * 29 + x * y * 3) % 97) / 96f;
                        pixels[y * 64 + x] = new Color(value, value, value, value);
                    }
                }
                texture.SetPixels(pixels);
                texture.Apply(true);
            }
            return texture;
        }

        void CreateProducer(Texture2D source, int layerCount, float tileSize)
        {
            var data = Own(new TerrainData { heightmapResolution = 33, alphamapResolution = 16, size = new Vector3(256, 10, 256) });
            var normal = Own(new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true));
            normal.SetPixels(new[] { new Color(1, .6f, 1, .65f), new Color(1, .6f, 1, .65f), new Color(1, .6f, 1, .65f), new Color(1, .6f, 1, .65f) });
            normal.Apply();
            var layers = new TerrainLayer[layerCount];
            for (int i = 0; i < layers.Length; ++i)
            {
                layers[i] = Own(new TerrainLayer
                {
                    diffuseTexture = source, normalMapTexture = normal, maskMapTexture = source,
                    tileSize = new Vector2(tileSize, tileSize), tileOffset = new Vector2(3 + i, 5 + i), normalScale = 1
                });
            }
            data.terrainLayers = layers;
            var weights = new float[16, 16, layerCount];
            for (int y = 0; y < 16; ++y)
            {
                for (int x = 0; x < 16; ++x)
                {
                    for (int i = 0; i < layerCount; ++i) { weights[y, x, i] = 1f / layerCount; }
                }
            }
            data.SetAlphamaps(0, 0, weights);
            var terrain = Own(Terrain.CreateTerrainGameObject(data)).GetComponent<Terrain>();
            m_Producer = new TerrainPageProducer(new[] { terrain }, new Rect(0, 0, 256, 256), Shader.Find("Hidden/VirtualTexture/TerrainBake"), EPageLayers.BaseColorNormalMask);
        }

        Color[][] Bake(Rect world, int size)
        {
            var textures = new RenderTexture[3];
            var targets = new RenderTargetIdentifier[3];
            try
            {
                for (int i = 0; i < textures.Length; ++i)
                {
                    textures[i] = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                    textures[i].Create();
                    targets[i] = textures[i];
                }
                using (var cmd = new CommandBuffer())
                {
                    cmd.SetRenderTarget(targets, targets[0]);
                    cmd.SetViewport(new Rect(0, 0, size, size));
                    cmd.ClearRenderTarget(false, true, Color.clear);
                    m_Producer.Bake(cmd, new PageBakeRequest { uvRect = new float4(world.x, world.y, world.width, world.height) / 256 });
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                var result = new Color[3][];
                for (int i = 0; i < result.Length; ++i)
                {
                    var readback = AsyncGPUReadback.Request(textures[i]);
                    readback.WaitForCompletion();
                    Assert.IsFalse(readback.hasError);
                    result[i] = readback.GetData<Color>().ToArray();
                }
                return result;
            }
            finally
            {
                foreach (var texture in textures)
                {
                    if (texture == null) { continue; }
                    texture.Release();
                    Object.DestroyImmediate(texture);
                }
            }
        }

        [Test]
        public void StochasticBakePreservesPageBordersAndMaterialAlignmentAcrossFiveLayers()
        {
            CreateProducer(Source(false), 5, 16);
            Color[][] left = Bake(new Rect(28, 60, 72, 72), 72);
            Color[][] right = Bake(new Rect(92, 60, 72, 72), 72);
            Color[][] shifted = Bake(new Rect(44, 60, 72, 72), 72);
            for (int y = 0; y < 72; ++y)
            {
                for (int x = 0; x < 8; ++x)
                {
                    for (int layer = 0; layer < 3; ++layer)
                    {
                        Color a = left[layer][y * 72 + 64 + x], b = right[layer][y * 72 + x];
                        for (int channel = 0; channel < 4; ++channel) { Assert.AreEqual(a[channel], b[channel], .005f, "Overlapping border texels"); }
                    }
                }
            }
            float difference = 0;
            for (int i = 0; i < 72 * 72; ++i)
            {
                float value = left[0][i].r;
                Assert.IsFalse(float.IsNaN(value) || float.IsInfinity(value));
                Assert.AreEqual(value, left[2][i].r, .005f, "Metallic shares color sampling");
                Assert.AreEqual(value, left[2][i].g, .005f, "AO shares color sampling");
                Assert.AreEqual(1 - value, left[2][i].a, .005f, "Roughness shares color sampling");
                Assert.AreEqual(.65f, left[1][i].a, .005f);
                Assert.AreEqual(.6f, left[1][i].g, .005f);
                difference += math.abs(value - shifted[0][i].r);
            }
            Assert.Greater(difference / (72 * 72), .01f, "A one-tile shift must not repeat the ordinary tiled pattern");
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(8)]
        public void StochasticBakeUsesContinuousGradientsToSelectSourceMip(int worldTexelSize)
        {
            CreateProducer(Source(true), 1, 64);
            Color[][] baked = Bake(new Rect(0, 0, 32 * worldTexelSize, 32 * worldTexelSize), 32);
            float expected = (math.log2(worldTexelSize) + 1) / 8;
            foreach (Color color in baked[0]) { Assert.AreEqual(expected, color.r, .003f); }
        }
    }
}
