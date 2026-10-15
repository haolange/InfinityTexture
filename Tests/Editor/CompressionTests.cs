using NUnit.Framework;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;

namespace Landscape.RuntimeVirtualTexture.Tests
{
    public class CompressionTests
    {
        const int SlotTexels = 256 + 2 * 4; // 264

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
            Assert.AreEqual(GraphicsFormat.RGBA_DXT5_SRGB, PageFormat.Of(EPageCompression.BC).PhysicalFormat(true));
            Assert.AreEqual(GraphicsFormat.RGBA_DXT5_UNorm, PageFormat.Of(EPageCompression.BC).PhysicalFormat(false));
            Assert.AreEqual(GraphicsFormat.RGBA_ETC2_SRGB, PageFormat.Of(EPageCompression.ETC2).PhysicalFormat(true));
            Assert.AreEqual(GraphicsFormat.RGBA_ETC2_UNorm, PageFormat.Of(EPageCompression.ETC2).PhysicalFormat(false));
            Assert.AreEqual(GraphicsFormat.R8G8B8A8_SRGB, PageFormat.Of(EPageCompression.None).PhysicalFormat(true));
            Assert.AreEqual(GraphicsFormat.R8G8B8A8_UNorm, PageFormat.Of(EPageCompression.None).PhysicalFormat(false));
        }

        [Test]
        public void SlotPacksIntoWhole128BitBlocks()
        {
            PageFormat bc = PageFormat.Of(EPageCompression.BC);
            Assert.IsTrue(bc.IsAligned(SlotTexels));
            Assert.AreEqual(new int2(66, 66), bc.BlocksPerSlot(SlotTexels));
            Assert.AreEqual(66 * 66 * 16, bc.SlotBytes(SlotTexels));
            Assert.AreEqual(SlotTexels * SlotTexels * 4, PageFormat.Of(EPageCompression.None).SlotBytes(SlotTexels));

            // BC3 / ETC2 RGBA8 都是每像素 1 字节。
            Assert.AreEqual(SlotTexels * SlotTexels, bc.SlotBytes(SlotTexels));
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
