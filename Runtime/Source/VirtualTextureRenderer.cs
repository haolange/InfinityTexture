using System;
using Unity.Collections;

namespace Landscape.RuntimeVirtualTexture
{
    internal class FVirtualTextureRenderer : IDisposable
    {
        private int m_limit;
        private NativeList<FPageRequestInfo> m_PageRequests;

        internal FVirtualTextureRenderer(in int limit = 8)
        {
            this.m_limit = limit;
            this.m_PageRequests = new NativeList<FPageRequestInfo>(256, Allocator.Persistent);
        }

        public void Dispose()
        {
            m_PageRequests.Dispose();
        }
    }
}
