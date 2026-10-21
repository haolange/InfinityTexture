using NUnit.Framework;
using Unity.Mathematics;
using System;
using System.Reflection;
using UnityEngine.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Landscape.RuntimeVirtualTexture.Tests
{
    public class FeedbackTests
    {
        [Test]
        public void R32RasterReadbackPreservesEveryPageIdBit()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsAsyncGPUReadback) { Assert.Ignore("Requires GPU readback"); }
            var texture = new RenderTexture(17, 9, GraphicsFormat.R32_UInt, GraphicsFormat.None);
            var material = new Material(Shader.Find("Hidden/VirtualTexture/Tests/Feedback"));
            using var cmd = new CommandBuffer();
            uint[] pixels = null;
            using var readback = new FeedbackReadback(data => pixels = data.ToArray());
            try
            {
                texture.Create();
                cmd.SetRenderTarget(texture);
                cmd.SetViewport(new Rect(0, 0, texture.width, texture.height));
                cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                readback.Request(cmd, texture);
                Graphics.ExecuteCommandBuffer(cmd);
                AsyncGPUReadback.WaitAllRequests();
                Assert.IsFalse(readback.inFlight);
                Assert.IsNotNull(pixels);
                Assert.AreEqual(texture.width * texture.height, pixels.Length);
                for (int y = 0; y < texture.height; ++y)
                for (int x = 0; x < texture.width; ++x)
                {
                    Assert.AreEqual(new PageId(x + 8175, y + 8183, 7).packed, pixels[y * texture.width + x]);
                }
            }
            finally { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(material); }
        }

        [Test]
        public void CancelledReadbackCannotReleaseANewerRequest()
        {
            using var readback = new FeedbackReadback(_ => Assert.Fail("过期回读不能提交反馈"));
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var generationField = typeof(FeedbackReadback).GetField("m_Generation", fields);
            var flightField = typeof(FeedbackReadback).GetField("m_InFlight", fields);
            object generation = generationField.GetValue(readback);
            var callback = (Action<AsyncGPUReadbackRequest>)generation.GetType().GetField("callback").GetValue(generation);

            readback.Cancel();
            flightField.SetValue(readback, true);
            callback(default);

            Assert.IsTrue(readback.inFlight, "旧回调不能清除新请求的在途标记");
            Assert.AreNotSame(generation, generationField.GetValue(readback));
            readback.Dispose();
            Assert.IsFalse(readback.inFlight);
        }

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
