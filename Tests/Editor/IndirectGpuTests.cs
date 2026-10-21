using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Mathematics;

namespace Landscape.RuntimeVirtualTexture.Tests
{
    internal class IndirectGpuTests
    {
        [Test]
        public void EveryMipUsesItsOwnWriteOffsetAndFineWritesOverrideAncestors()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsAsyncGPUReadback) { Assert.Ignore("Requires GPU readback"); }
            var map = new IndirectMap(4, 4, Shader.Find("Hidden/VirtualTexture/IndirectMap"));
            using var writes = new NativeList<IndirectWrite>(5, Allocator.Temp);
            using var ranges = new NativeArray<int2>(new[] { new int2(0, 2), new int2(2, 2), new int2(4, 1) }, Allocator.Temp);
            using var cmd = new CommandBuffer();
            uint root = VirtualTextureMath.IndirectValue(new int2(1), 2);
            uint fine = VirtualTextureMath.IndirectValue(new int2(2, 0), 0);
            uint middle = VirtualTextureMath.IndirectValue(new int2(0, 1), 1);
            writes.Add(new IndirectWrite { rect = new int4(0, 0, 4, 4), value = root });
            writes.Add(new IndirectWrite { rect = new int4(1, 2, 1, 1), value = fine });
            writes.Add(new IndirectWrite { rect = new int4(0, 0, 2, 2), value = root });
            writes.Add(new IndirectWrite { rect = new int4(0, 1, 1, 1), value = middle });
            writes.Add(new IndirectWrite { rect = new int4(0, 0, 1, 1), value = root });
            try
            {
                map.Update(cmd, writes, ranges); Graphics.ExecuteCommandBuffer(cmd);
                for (int mip = 0; mip < 3; ++mip)
                {
                    var read = AsyncGPUReadback.Request(Shader.GetGlobalTexture("_VTIndirectMap"), mip, TextureFormat.RGBA32);
                    read.WaitForCompletion(); Assert.IsFalse(read.hasError);
                    var pixels = read.GetData<Color32>(); int size = 4 >> mip;
                    for (int y = 0; y < size; ++y)
                    for (int x = 0; x < size; ++x)
                    {
                        uint expected = mip == 0 && x == 1 && y == 2 ? fine : mip == 1 && x == 0 && y == 1 ? middle : root;
                        Color32 actual = pixels[y * size + x];
                        Assert.AreEqual(expected, (uint)actual.r | (uint)actual.g << 8 | (uint)actual.b << 16 | (uint)actual.a << 24, $"mip {mip}, {x},{y}");
                    }
                }
            }
            finally { map.Dispose(); }
        }
    }
}
