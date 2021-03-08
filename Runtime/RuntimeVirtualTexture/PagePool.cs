namespace Landscape.ProceduralVirtualTexture
{
    public class PagePool
    {
        public struct NodeInfo
        {
            public int id;
            public int Next;
            public int Prev;
        }

        private NodeInfo head;
        private NodeInfo tail;
        private NodeInfo[] allNodes;

        public int First { get { return head.id; } }

        public void Init(int count)
        {
            allNodes = new NodeInfo[count];
            for (int i= 0;i < count;i++)
            {
                allNodes[i] = new NodeInfo()
                {
                    id = i,
                };
            }
            for (int i = 0; i < count; i++)
            {
                allNodes[i].Prev = (i != 0) ? allNodes[i - 1].id : 0;
                allNodes[i].Next = (i + 1 < count) ? allNodes[i + 1].id : count - 1;
            }
            head = allNodes[0];
            tail = allNodes[count - 1];
        }

        public bool SetActive(int id)
        {
            if (id < 0 || id >= allNodes.Length)
                return false;

            ref NodeInfo node = ref allNodes[id];
            if(node.id == tail.id)
            {
                return true;
            }

            Remove(ref node);
            AddLast(ref node);
            return true;
        }

        private void AddLast(ref NodeInfo node)
        {
            ref NodeInfo lastTail = ref allNodes[tail.id];
            tail = node;

            lastTail.Next = node.id;
            //allNodes[lastTail.id].Next = node.id;
            allNodes[lastTail.Next] = node;

            node.Prev = lastTail.id;
            //allNodes[node.id].Prev = lastTail.id;
            allNodes[node.Prev] = lastTail;
        }

        private void Remove(ref NodeInfo node)
        {
            if (head.id == node.id)
            {
                head = allNodes[node.Next];
            } else {
                ref NodeInfo Prev = ref allNodes[node.Prev];
                ref NodeInfo Next = ref allNodes[node.Next];

                Prev.Next = node.Next;
                Next.Prev = node.Prev;

                allNodes[Prev.Next] = Next;
                allNodes[Next.Prev] = Prev;
            }
        }
    }
}