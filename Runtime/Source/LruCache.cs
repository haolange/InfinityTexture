using System;
using Unity.Collections;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.RuntimeVirtualTexture
{
    internal struct FNodeInfo
    {
        public int id;
        public int nextID;
        public int prevID;
    }

#if UNITY_EDITOR
    internal unsafe sealed class FLruCacheDebugView
    {
        FLruCache m_Target;

        public FLruCacheDebugView(FLruCache target)
        {
            m_Target = target;
        }

        public int m_Length
        {
            get
            {
                return m_Target.m_Length;
            }
        }

        public FNodeInfo m_HeadNodeInfo
        {
            get
            {
                return m_Target.m_HeadNodeInfo;
            }
        }

        public FNodeInfo m_TailNodeInfo
        {
            get
            {
                return m_Target.m_TailNodeInfo;
            }
        }

        public List<FNodeInfo> m_NodeInfoList
        {
            get
            {
                var result = new List<FNodeInfo>();
                for (int i = 0; i < m_Target.m_Length; ++i)
                {
                    result.Add(m_Target.m_NodeInfoList[i]);
                }
                return result;
            }
        }
    }

    [DebuggerTypeProxy(typeof(FLruCacheDebugView))]
#endif
    internal unsafe struct FLruCache : IDisposable
    {
        internal int m_Length;
        internal FNodeInfo m_HeadNodeInfo;
        internal FNodeInfo m_TailNodeInfo;
        internal FNodeInfo* m_NodeInfoList;
        public int First { get { return m_HeadNodeInfo.id; } }


        public FLruCache(in int count)
        {
            m_Length = count;
            m_NodeInfoList = (FNodeInfo*)UnsafeUtility.Malloc(Marshal.SizeOf(typeof(FNodeInfo)) * count, 64, Allocator.Persistent);

            for (int i = 0; i < count; ++i)
            {
                m_NodeInfoList[i] = new FNodeInfo()
                {
                    id = i,
                };
            }
            for (int j = 0; j < count; ++j)
            {
                m_NodeInfoList[j].prevID = (j != 0) ? m_NodeInfoList[j - 1].id : 0;
                m_NodeInfoList[j].nextID = (j + 1 < count) ? m_NodeInfoList[j + 1].id : count - 1;
            }
            m_HeadNodeInfo = m_NodeInfoList[0];
            m_TailNodeInfo = m_NodeInfoList[count - 1];
        }

        public void Dispose()
        {
            UnsafeUtility.Free((void*)m_NodeInfoList, Allocator.Persistent);
        }

        public bool SetActive(int id)
        {
            if (id < 0 || id >= m_Length)
                return false;

            ref FNodeInfo nodeInfo = ref m_NodeInfoList[id];
            if (nodeInfo.id == m_TailNodeInfo.id)
            {
                return true;
            }

            Remove(ref nodeInfo);
            AddLast(ref nodeInfo);
            return true;
        }

        private void AddLast(ref FNodeInfo nodeInfo)
        {
            ref FNodeInfo lastNodeInfo = ref m_NodeInfoList[m_TailNodeInfo.id];
            m_TailNodeInfo = nodeInfo;

            lastNodeInfo.nextID = nodeInfo.id;
            m_NodeInfoList[lastNodeInfo.nextID] = nodeInfo;

            nodeInfo.prevID = lastNodeInfo.id;
            m_NodeInfoList[nodeInfo.prevID] = lastNodeInfo;
        }

        private void Remove(ref FNodeInfo nodeInfo)
        {
            if (m_HeadNodeInfo.id == nodeInfo.id)
            {
                m_HeadNodeInfo = m_NodeInfoList[nodeInfo.nextID];
            } else {
                ref FNodeInfo prevNodeInfo = ref m_NodeInfoList[nodeInfo.prevID];
                ref FNodeInfo nextNodeInfo = ref m_NodeInfoList[nodeInfo.nextID];
                prevNodeInfo.nextID = nodeInfo.nextID;
                nextNodeInfo.prevID = nodeInfo.prevID;
                m_NodeInfoList[prevNodeInfo.nextID] = nextNodeInfo;
                m_NodeInfoList[nextNodeInfo.prevID] = prevNodeInfo;
            }
        }
    }
}
