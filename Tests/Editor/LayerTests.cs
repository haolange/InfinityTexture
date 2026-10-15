using NUnit.Framework;

namespace Landscape.RuntimeVirtualTexture.Tests
{
    public class LayerTests
    {
        [Test]
        public void Presets_EnableLayersFromFewToMany()
        {
            CollectionAssert.AreEqual(new[] { EPageLayer.BaseColor }, PhysicalLayers.Enabled(EPageLayers.BaseColor));
            CollectionAssert.AreEqual(new[] { EPageLayer.BaseColor, EPageLayer.Normal }, PhysicalLayers.Enabled(EPageLayers.BaseColorNormal));
            CollectionAssert.AreEqual(new[] { EPageLayer.BaseColor, EPageLayer.Normal, EPageLayer.Mask }, PhysicalLayers.Enabled(EPageLayers.BaseColorNormalMask));
        }

        [Test]
        public void OnlyBaseColorIsAColorLayer()
        {
            Assert.IsTrue(PhysicalLayers.IsColor(EPageLayer.BaseColor));
            Assert.IsFalse(PhysicalLayers.IsColor(EPageLayer.Normal));
            Assert.IsFalse(PhysicalLayers.IsColor(EPageLayer.Mask));
        }
    }
}
