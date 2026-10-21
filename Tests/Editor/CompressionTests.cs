using NUnit.Framework;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;

namespace Landscape.RuntimeVirtualTexture.Tests
{
    public class CompressionTests
    {
        const int SlotTexels = 256 + 2 * 4; // 264

        [TestCase(0, 256, 8, 512, 30)]
        [TestCase(16, 0, 8, 512, 30)]
        [TestCase(16, 256, 0, 512, 30)]
        [TestCase(16, 256, 8, 0, 30)]
        [TestCase(16, 256, 8, 512, 0)]
        public void InvalidAllocationAndQueueLimitsAreRejected(int poolSize, int pageTexels, int bakes, int queue, int timeout)
        {
            var asset = ScriptableObject.CreateInstance<VirtualTextureAsset>();
            try
            {
                asset.poolSize = poolSize;
                asset.pageTexels = pageTexels;
                asset.bakesPerRound = bakes;
                asset.queueCapacity = queue;
                asset.queueTimeout = timeout;
                Assert.IsFalse(asset.Validate(out string error));
                StringAssert.DoesNotContain("shader", error);
            }
            finally { Object.DestroyImmediate(asset); }
        }

        [Test]
        public void InvalidAstcBlockIsRejectedBeforeAlignmentDivision()
        {
            var asset = ScriptableObject.CreateInstance<VirtualTextureAsset>();
            try
            {
                asset.compression = EPageCompression.ASTC;
                asset.astcBlock = (EAstcBlock)0;
                Assert.IsFalse(asset.Validate(out string error));
                StringAssert.Contains("astcBlock", error);
            }
            finally { Object.DestroyImmediate(asset); }
        }

        [Test]
        public void FixedRateFormatsUseFourByFourBlocks()
        {
            Assert.AreEqual(new int2(1, 1), PageFormat.Of(EPageCompression.None).blockSize);
            Assert.AreEqual(new int2(4, 4), PageFormat.Of(EPageCompression.BC).blockSize);
            Assert.AreEqual(new int2(4, 4), PageFormat.Of(EPageCompression.ETC2).blockSize);
        }

        [Test]
        public void ColorLayersAreSrgbDataLayersAreNot()
        {
            Assert.AreEqual(GraphicsFormat.RGBA_DXT1_SRGB, PageFormat.Of(EPageCompression.BC).ForLayer(EPageLayer.BaseColor).physicalFormat);
            Assert.AreEqual(GraphicsFormat.RGBA_DXT5_UNorm, PageFormat.Of(EPageCompression.BC).ForLayer(EPageLayer.Mask).physicalFormat);
            Assert.AreEqual(GraphicsFormat.RGB_ETC2_SRGB, PageFormat.Of(EPageCompression.ETC2).ForLayer(EPageLayer.BaseColor).physicalFormat);
            Assert.AreEqual(GraphicsFormat.RGBA_ETC2_UNorm, PageFormat.Of(EPageCompression.ETC2).ForLayer(EPageLayer.Mask).physicalFormat);
            Assert.AreEqual(GraphicsFormat.R8G8B8A8_SRGB, PageFormat.Of(EPageCompression.None).ForLayer(EPageLayer.BaseColor).physicalFormat);
            Assert.AreEqual(GraphicsFormat.R8G8B8A8_UNorm, PageFormat.Of(EPageCompression.None).ForLayer(EPageLayer.Mask).physicalFormat);
        }

        [Test]
        public void SlotPacksIntoWhole128BitBlocks()
        {
            PageFormat bc = PageFormat.Of(EPageCompression.BC);
            Assert.IsTrue(bc.IsAligned(SlotTexels));
            Assert.AreEqual(new int2(66, 66), bc.BlocksPerSlot(SlotTexels));
            Assert.AreEqual(66 * 66 * 8, bc.SlotBytes(SlotTexels));
            Assert.AreEqual(SlotTexels * SlotTexels * 4, PageFormat.Of(EPageCompression.None).SlotBytes(SlotTexels));

            // BC 颜色层 BC1 每像素半字节，数据层每像素一字节。
            Assert.AreEqual(SlotTexels * SlotTexels / 2, bc.SlotBytes(SlotTexels));
            Assert.AreEqual(SlotTexels * SlotTexels, bc.ForLayer(EPageLayer.Normal).SlotBytes(SlotTexels));
        }

        [Test]
        public void PerLayerFormatsUseMatchingBlockWidths()
        {
            var bc = PageFormat.Of(EPageCompression.BC);
            Assert.AreEqual(GraphicsFormat.RGBA_DXT1_SRGB, bc.physicalFormat);
            Assert.AreEqual(GraphicsFormat.RG_BC5_UNorm, bc.ForLayer(EPageLayer.Normal).physicalFormat);
            Assert.AreEqual(GraphicsFormat.RGBA_DXT5_UNorm, bc.ForLayer(EPageLayer.Mask).physicalFormat);
            Assert.AreEqual(GraphicsFormat.R32G32_UInt, bc.blockFormat);
            Assert.AreEqual(GraphicsFormat.R32G32B32A32_UInt, bc.ForLayer(EPageLayer.Normal).blockFormat);
            Assert.AreEqual(8, bc.blockBytes);
            Assert.AreEqual(16, bc.ForLayer(EPageLayer.Normal).blockBytes);
            Assert.AreEqual(SlotTexels * SlotTexels * 5 / 2, bc.SlotBytes(SlotTexels) + bc.ForLayer(EPageLayer.Normal).SlotBytes(SlotTexels) + bc.ForLayer(EPageLayer.Mask).SlotBytes(SlotTexels));
            var etc = PageFormat.Of(EPageCompression.ETC2);
            Assert.AreEqual(GraphicsFormat.RGB_ETC2_SRGB, etc.physicalFormat);
            Assert.AreEqual(8, etc.blockBytes);
            Assert.AreEqual(16, etc.ForLayer(EPageLayer.Normal).blockBytes);
        }

        [Test]
        public void MisalignedSlotIsRejected()
        {
            Assert.IsFalse(PageFormat.Of(EPageCompression.ETC2).IsAligned(256 + 2 * 1));
            Assert.IsTrue(PageFormat.Of(EPageCompression.None).IsAligned(256 + 2 * 1));
        }

        [Test]
        public void AssetValidationReportsMisalignedSlot()
        {
            var asset = ScriptableObject.CreateInstance<VirtualTextureAsset>();
            try
            {
                asset.pageTexels = 256;
                asset.pageBorder = EBorder.X1;
                asset.compression = EPageCompression.BC;
                Assert.IsFalse(asset.Validate(out string error));
                StringAssert.Contains("258", error);
            }
            finally { Object.DestroyImmediate(asset); }
        }
    }
}
