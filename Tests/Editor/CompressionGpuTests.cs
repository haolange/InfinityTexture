using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;
using Object = UnityEngine.Object;

namespace Landscape.RuntimeVirtualTexture.Tests
{
    internal class CompressionGpuTests
    {
        [TestCase(EPageCompression.BC, EAstcBlock.X4)]
        [TestCase(EPageCompression.ETC2, EAstcBlock.X4)]
        [TestCase(EPageCompression.ASTC, EAstcBlock.X4)]
        [TestCase(EPageCompression.ASTC, EAstcBlock.X6)]
        [TestCase(EPageCompression.ASTC, EAstcBlock.X8)]
        public void EncodedPagesCopyToNonzeroSlotsAndCanBeOverwritten(EPageCompression compression, EAstcBlock block)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsAsyncGPUReadback) { Assert.Ignore("Requires GPU readback"); }
            var asset = ScriptableObject.CreateInstance<VirtualTextureAsset>();
            asset.poolSize = 2; asset.pageTexels = 16; asset.pageBorder = EBorder.X4; asset.compression = compression; asset.astcBlock = block;
            asset.indirectShader = Shader.Find("Hidden/VirtualTexture/IndirectMap");
            asset.encodeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.virtual-texture/Shaders/Compress/PageEncode.compute");
            PhysicalLayers actual = null, reference = null;
            var cmd = new CommandBuffer();
            try
            {
                if (!VirtualTextureSystem.CanCreate(asset, out string reason)) { Assert.Ignore(reason); }
                var layers = PhysicalLayers.Enabled(EPageLayers.BaseColorNormalMask);
                actual = new PhysicalLayers(asset, layers, asset.format, asset.encodeShader);
                var physical = new Texture[3];
                var names = new[] { "_VTPhysicalBaseColor", "_VTPhysicalNormal", "_VTPhysicalMask" };
                for (int i = 0; i < 3; ++i) { physical[i] = Shader.GetGlobalTexture(names[i]); }
                reference = new PhysicalLayers(asset, layers, PageFormat.Of(EPageCompression.None), null);
                var expected = new Texture[3];
                for (int i = 0; i < 3; ++i) { expected[i] = Shader.GetGlobalTexture(names[i]); }
                var colors = new[] { new Color(.2f, .6f, .8f, .4f), new Color(.7f, .3f, .4f, .65f), new Color(.4f, .55f, .3f, .7f) };
                for (int pass = 0; pass < colors.Length; ++pass)
                {
                    var slot = pass == 0 ? new int2(0) : new int2(1);
                    cmd.Clear();
                    actual.BindScratch(cmd); cmd.ClearRenderTarget(false, true, colors[pass]); actual.Encode(cmd, slot);
                    reference.BindScratch(cmd); cmd.ClearRenderTarget(false, true, colors[pass]); reference.Encode(cmd, slot);
                    Graphics.ExecuteCommandBuffer(cmd);
                    for (int layer = 0; layer < 3; ++layer)
                    {
                        Color[] data = Read(physical[layer]), baseline = Read(expected[layer]);
                        int begin = pass == 0 ? 0 : asset.slotTexels;
                        for (int y = begin; y < begin + asset.slotTexels; ++y)
                        for (int x = begin; x < begin + asset.slotTexels; ++x)
                        {
                            int index = y * asset.poolTexels + x;
                            Color a = data[index], b = baseline[index];
                            if (layer == 0) { Assert.LessOrEqual(Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b)), .06f); }
                            else if (layer == 1)
                            {
                                float normalX = compression == EPageCompression.BC ? a.r : a.a;
                                Assert.LessOrEqual(Mathf.Abs(normalX - b.a), .035f);
                                Assert.LessOrEqual(Mathf.Abs(a.g - b.g), .06f);
                            }
                            else { Assert.LessOrEqual(Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b),Mathf.Abs(a.a-b.a)), .06f); }
                        }
                        // 后续槽更新不得破坏首槽。
                        if (pass > 0)
                        {
                            Color a = data[0], b = baseline[0];
                            Assert.LessOrEqual(Mathf.Abs(a.g - b.g), .06f);
                        }
                    }
                }
            }
            finally { cmd.Dispose(); actual?.Dispose(); reference?.Dispose(); Object.DestroyImmediate(asset); }
        }

        static Color[] Read(Texture texture)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.virtual-texture/Tests/Editor/CompressionTest.compute");
            using var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, texture.width * texture.height, 16);
            shader.SetTexture(0, "_Input", texture); shader.SetBuffer(0, "_Output", buffer); shader.SetInt("_Width", texture.width);
            shader.Dispatch(0, (texture.width + 7) / 8, (texture.height + 7) / 8, 1);
            var read = AsyncGPUReadback.Request(buffer); read.WaitForCompletion(); Assert.IsFalse(read.hasError);
            return read.GetData<Color>().ToArray();
        }
    }
}
