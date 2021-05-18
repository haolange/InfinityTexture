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

        public int Length
        {
            get
            {
                return m_Target.length;
            }
        }

        public FNodeInfo HeadNode
        {
            get
            {
                return m_Target.headNodeInfo;
            }
        }

        public FNodeInfo TailNode
        {
            get
            {
                return m_Target.tailNodeInfo;
            }
        }

        public List<FNodeInfo> NodeInfos
        {
            get
            {
                var result = new List<FNodeInfo>();
                for (int i = 0; i < m_Target.length; ++i)
                {
                    result.Add(m_Target.nodeInfoList[i]);
                }
                return result;
            }
        }
    }

    [DebuggerTypeProxy(typeof(FLruCacheDebugView))]
#endif
    internal unsafe struct FLruCache : IDisposable
    {
        internal int length;
        internal FNodeInfo headNodeInfo;
        internal FNodeInfo tailNodeInfo;
        [NativeDisableUnsafePtrRestriction]
        internal FNodeInfo* nodeInfoList;
        internal int First { get { return headNodeInfo.id; } }

        public FLruCache(in int length)
        {
            this.length = length;
            this.nodeInfoList = (FNodeInfo*)UnsafeUtility.Malloc(Marshal.SizeOf(typeof(FNodeInfo)) * length, 64, Allocator.Persistent);

            for (int i = 0; i < length; ++i)
            {
                nodeInfoList[i] = new FNodeInfo()
                {
                    id = i,
                };
            }
            for (int j = 0; j < length; ++j)
            {
                nodeInfoList[j].prevID = (j != 0) ? nodeInfoList[j - 1].id : 0;
                nodeInfoList[j].nextID = (j + 1 < length) ? nodeInfoList[j + 1].id : length - 1;
            }
            this.headNodeInfo = nodeInfoList[0];
            this.tailNodeInfo = nodeInfoList[length - 1];
        }

        public static void BuildLruCache(ref FLruCache lruCache, in int count)
        {
            lruCache.length = count;
            lruCache.nodeInfoList = (FNodeInfo*)UnsafeUtility.Malloc(Marshal.SizeOf(typeof(FNodeInfo)) * count, 64, Allocator.Persistent);

            for (int i = 0; i < count; ++i)
            {
                lruCache.nodeInfoList[i] = new FNodeInfo()
                {
                    id = i,
                };
            }
            for (int j = 0; j < count; ++j)
            {
                lruCache.nodeInfoList[j].prevID = (j != 0) ? lruCache.nodeInfoList[j - 1].id : 0;
                lruCache.nodeInfoList[j].nextID = (j + 1 < count) ? lruCache.nodeInfoList[j + 1].id : count - 1;
            }
            lruCache.headNodeInfo = lruCache.nodeInfoList[0];
            lruCache.tailNodeInfo = lruCache.nodeInfoList[count - 1];
        }

        public void Dispose()
        {
            UnsafeUtility.Free((void*)nodeInfoList, Allocator.Persistent);
        }

        public bool SetActive(in int id)
        {
            if (id < 0 || id >= length) { return false; }

            ref FNodeInfo nodeInfo = ref nodeInfoList[id];
            if (nodeInfo.id == tailNodeInfo.id) { return true; }

            Remove(ref nodeInfo);
            AddLast(ref nodeInfo);
            return true;
        }

        private void AddLast(ref FNodeInfo nodeInfo)
        {
            ref FNodeInfo lastNodeInfo = ref nodeInfoList[tailNodeInfo.id];
            tailNodeInfo = nodeInfo;

            lastNodeInfo.nextID = nodeInfo.id;
            nodeInfoList[lastNodeInfo.nextID] = nodeInfo;

            nodeInfo.prevID = lastNodeInfo.id;
            nodeInfoList[nodeInfo.prevID] = lastNodeInfo;
        }

        private void Remove(ref FNodeInfo nodeInfo)
        {
            if (headNodeInfo.id == nodeInfo.id)
            {
                headNodeInfo = nodeInfoList[nodeInfo.nextID];
            }
            else
            {
                ref FNodeInfo prevNodeInfo = ref nodeInfoList[nodeInfo.prevID];
                ref FNodeInfo nextNodeInfo = ref nodeInfoList[nodeInfo.nextID];
                prevNodeInfo.nextID = nodeInfo.nextID;
                nextNodeInfo.prevID = nodeInfo.prevID;
                nodeInfoList[prevNodeInfo.nextID] = nextNodeInfo;
                nodeInfoList[nextNodeInfo.prevID] = prevNodeInfo;
            }
        }
    }
}