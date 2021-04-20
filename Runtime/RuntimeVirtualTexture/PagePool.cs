using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.ProceduralVirtualTexture
{
    internal struct FNodeInfo
    {
        public int id;
        public int nextID;
        public int prevID;
    }

    public unsafe struct FLruCache
    {
        private int m_Length;
        private FNodeInfo m_HeadNodeInfo;
        private FNodeInfo m_TailNodeInfo;
        private FNodeInfo* m_NodeInfoList;
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

        public void Release()
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
            }
            else
            {
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