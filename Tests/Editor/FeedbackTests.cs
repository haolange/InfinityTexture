using NUnit.Framework;
using Unity.Mathematics;

namespace Landscape.RuntimeVirtualTexture.Tests
{
    public class FeedbackTests
    {
        [Test]
        public void Size_IsRenderResolutionOverSixteenRoundedUp()
        {
            Assert.AreEqual(new int2(120, 68), FeedbackLayout.Size(new int2(1920, 1080)));
            Assert.AreEqual(new int2(1, 1), FeedbackLayout.Size(new int2(7, 1)));
        }

        [Test]
        public void Scale_IsTheActualPerAxisDownscale()
        {
            float2 scale = FeedbackLayout.Scale(new int2(1920, 1080));
            Assert.AreEqual(16f, scale.x, 1e-5f);
            Assert.AreEqual(1080f / 68f, scale.y, 1e-5f);
        }

        /// <summary>着色器 VTPackPageId 用的是同一布局；这里固定住位布局。</summary>
        [Test]
        public void PageId_MatchesShaderBitLayout()
        {
            var page = new PageId(4095, 1234, 12);
            Assert.AreEqual(0x80000000u | (12u << 26) | (1234u << 13) | 4095u, page.packed);
            Assert.AreEqual(page, new PageId(page.packed));
        }
    }
}
