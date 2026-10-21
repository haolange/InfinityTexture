using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Landscape.RuntimeVirtualTexture.Tests
{
    public class FilterTests
    {
        [Test]
        public void TemporalMipDefaultsOffAndRequiresPixelScaleJitter()
        {
            var asset = ScriptableObject.CreateInstance<VirtualTextureAsset>();
            try { Assert.IsFalse(asset.temporalMip); }
            finally { Object.DestroyImmediate(asset); }
            Matrix4x4 p = Matrix4x4.Perspective(60, 16f / 9, .1f, 1000);
            Assert.IsFalse(VirtualTextureMath.IsPixelJitter(p, p, 1920, 1080));
            Matrix4x4 jitter = Matrix4x4.Translate(new Vector3(.5f / 1920, -.5f / 1080, 0));
            Assert.IsTrue(VirtualTextureMath.IsPixelJitter(jitter * p, p, 1920, 1080));
            Assert.IsFalse(VirtualTextureMath.IsPixelJitter(Matrix4x4.Scale(new Vector3(1.1f, 1, 1)) * p, p, 1920, 1080));
            Assert.IsFalse(VirtualTextureMath.IsPixelJitter(Matrix4x4.Translate(new Vector3(.01f, 0, 0)) * p, p, 1920, 1080));
            Assert.IsFalse(VirtualTextureMath.IsPixelJitter(p, Matrix4x4.zero, 1920, 1080));
        }

        [TestCase(0, 0, 10, 10, true)]
        [TestCase(2, 2, 4, 4, true)]
        [TestCase(-1, 2, 4, 4, false)]
        [TestCase(8, 2, 4, 4, false)]
        [TestCase(20, 20, 4, 4, false)]
        public void OnlyFullyCoveredSurfacesSwitchToVirtualTexturing(float x, float y, float width, float height, bool covered)
        {
            Assert.AreEqual(covered, TerrainVirtualTexture.Covers(new Rect(0, 0, 10, 10), new Rect(x, y, width, height)));
        }

        [Test]
        public void FilterTapsClampAtTheDomainEdgesInsteadOfWrappingInsideThePage()
        {
            float2 low = VirtualTextureMath.PhysicalTexel(new float2(-.01f), new int2(0), 4, 16, 256, 4);
            float2 high = VirtualTextureMath.PhysicalTexel(new float2(1.01f), new int2(0), 4, 16, 256, 4);
            Assert.AreEqual(4, low.x);
            Assert.AreEqual(4, low.y);
            Assert.Greater(high.x, 259.99f);
            Assert.Less(high.x, 260);
            Assert.AreEqual(high.x, high.y);
        }

        [Test]
        public void RequestMip_BilinearRoundsOthersFloor()
        {
            Assert.AreEqual(3, VirtualTextureMath.RequestMip(2.6f, EPageFilter.Bilinear, 10));
            Assert.AreEqual(2, VirtualTextureMath.RequestMip(2.4f, EPageFilter.Bilinear, 10));
            Assert.AreEqual(2, VirtualTextureMath.RequestMip(2.6f, EPageFilter.Trilinear, 10));
            Assert.AreEqual(2, VirtualTextureMath.RequestMip(2.6f, EPageFilter.Anisotropic, 10));
        }

        [Test]
        public void RequestMip_ClampsToValidLevels()
        {
            Assert.AreEqual(0, VirtualTextureMath.RequestMip(-3f, EPageFilter.Trilinear, 10));
            Assert.AreEqual(10, VirtualTextureMath.RequestMip(14f, EPageFilter.Bilinear, 10));
        }

        [Test]
        public void IsotropicLodUsesTheLongerAxis()
        {
            float lod = VirtualTextureMath.FilterLod(new float2(8, 0), new float2(0, 2), EPageFilter.Trilinear, 4, out int taps);
            Assert.AreEqual(3f, lod, 1e-5f);
            Assert.AreEqual(1, taps);
        }

        [Test]
        public void AnisotropicLodSplitsTheMajorAxisAcrossTaps()
        {
            // 8:1 的足迹、最多 4 次：每次覆盖 8 / 4 = 2 个虚拟像素。
            float lod = VirtualTextureMath.FilterLod(new float2(8, 0), new float2(0, 1), EPageFilter.Anisotropic, 4, out int taps);
            Assert.AreEqual(4, taps);
            Assert.AreEqual(1f, lod, 1e-5f);

            // 3:1 的足迹只需要 3 次，每次正好覆盖短轴。
            lod = VirtualTextureMath.FilterLod(new float2(0, 6), new float2(2, 0), EPageFilter.Anisotropic, 4, out taps);
            Assert.AreEqual(3, taps);
            Assert.AreEqual(1f, lod, 1e-5f);
        }
    }
}
