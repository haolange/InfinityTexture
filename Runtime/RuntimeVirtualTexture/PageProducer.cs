using System;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace Landscape.ProceduralVirtualTexture
{
    public unsafe class FPageProducer
	{
        public PageTable[] PageTable;

        public Dictionary<Vector2Int, int3> ActivePages = new Dictionary<Vector2Int, int3>();


        private RuntimeVirtualTexture pageTexture;

        private FPageRenderer pageRenderer;


        private Mesh DrawPageMesh;

        private Material DrawPageTableMaterial;

        private MaterialPropertyBlock DrawPageTableBlock;

        NativeList<DrawPageInfo> drawList;


        public void Initialize(Mesh InDrawPageMesh, FPageRenderer InPageRenderer, RuntimeVirtualTexture InPageTexture)
        {
            DrawPageMesh = InDrawPageMesh;
            pageTexture = InPageTexture;
            pageRenderer = InPageRenderer;

            PageTable = new PageTable[pageTexture.MaxMipLevel + 1];
            for (int i = 0; i <= pageTexture.MaxMipLevel; i++)
            {
                PageTable[i] = new PageTable(i, pageTexture.PageSize);
            }

            DrawPageTableBlock = new MaterialPropertyBlock();
            DrawPageTableMaterial = new Material(Shader.Find("VirtualTexture/DrawPageTable"));
            DrawPageTableMaterial.enableInstancing = true;

            ActivatePage(0, 0, pageTexture.MaxMipLevel);

            drawList = new NativeList<DrawPageInfo>(256, Allocator.Persistent);
        }

        public void Release()
        {
            drawList.Dispose();
        }

        public void Reset()
        {
            for (int i = 0; i <= pageTexture.MaxMipLevel; ++i)
            {
                PageTable pageTable = PageTable[i];

                for (int j = 0; j < pageTable.cellCount; ++j)
                {
                    for (int k = 0; k < pageTable.cellCount; k++)
                    {
                        InvalidatePage(pageTable.pageBuffer[j * pageTable.cellCount + k].Payload.TileIndex);
                    }
                }
            }
            ActivePages.Clear();
        }

        private void LoadPage(in int x, in int y, ref FPage Page)
        {
            if (Page.bNull == true)
                return;

            // 正在加载中,不需要重复请求
            if (Page.Payload.pageRequestInfo.bNull == false)
                return;

            // 新建加载请求
            pageRenderer.AllocateRquestInfo(x, y, Page.MipLevel, ref Page.Payload.pageRequestInfo);
        }

        private void ActivatePage(in int x, in int y, in int mip)
        {
            if (mip > pageTexture.MaxMipLevel || mip < 0 || x < 0 || y < 0 || x >= pageTexture.PageSize || y >= pageTexture.PageSize)
                return;

            // 找到当前页表
            ref FPage Page = ref PageTable[mip].GetPage(x, y);

            if (Page.bNull == true) { return; }

            if (!Page.Payload.IsReady)
            {
                LoadPage(x, y, ref Page);

                //向上找到最近的父节点
                /*while(mip < pageTexture.MaxMipLevel && !Page.Payload.IsReady)
                {
                    mip++;
                    Page = ref PageTable[mip].GetPage(x, y);
                }*/
            }

            if (Page.Payload.IsReady)
            {
                // 激活对应的平铺贴图块
                Page.Payload.ActiveFrame = Time.frameCount;
                pageTexture.SetActive(Page.Payload.TileIndex);
                return;
            }

            return;
        }


        public void ProcessFeedback(in NativeArray<Color32> FeedbackData)
        {
            // 激活对应页表
            for (int i = 0; i < FeedbackData.Length; ++i)
            {
                Color32 Feedback = FeedbackData[i];
                ActivatePage(Feedback.r, Feedback.g, Feedback.b);
            }

            DrawPageTable();
        }

        private struct DrawPageInfo : IComparable<DrawPageInfo>
        {
            public int mip;
            public Rect rect;
            public Vector2 drawPos;

            public int CompareTo(DrawPageInfo target)
            {
                return -(mip.CompareTo(target.mip));
            }
        }

        private void DrawPageTable()
        {
            // 将页表数据写入页表贴图
            var currentFrame = (byte)Time.frameCount;
            drawList.Clear();

            foreach (var kv in ActivePages)
            {
                PageTable pageTable = PageTable[kv.Value.z];
                ref FPage page = ref pageTable.GetPage(kv.Value.x, kv.Value.y);

                // 只写入当前帧活跃的页表
                if (page.Payload.ActiveFrame != Time.frameCount)
                    continue;

                var table = PageTable[page.MipLevel];
                var lb = new Vector2Int(page.Rect.xMin, page.Rect.yMin);
                while (lb.x < 0)
                {
                    lb.x += pageTexture.PageSize;
                }
                while (lb.y < 0)
                {
                    lb.y += pageTexture.PageSize;
                }

                drawList.Add(new DrawPageInfo() {
                    rect = new Rect(lb.x, lb.y, page.Rect.width, page.Rect.height),
                    mip = page.MipLevel,
                    drawPos = new Vector2((float)page.Payload.TileIndex.x / 255,
                    (float)page.Payload.TileIndex.y / 255),
                });
            }

            if (drawList.Length == 0) { return; }
            drawList.Sort();

            NativeArray<Vector4> PageInfos = new NativeArray<Vector4>(drawList.Length, Allocator.TempJob);
            NativeArray<Matrix4x4> Materix_MVP = new NativeArray<Matrix4x4>(drawList.Length, Allocator.TempJob);

            for (int i = 0; i < drawList.Length; ++i)
            {
                float size = drawList[i].rect.width / pageTexture.PageSize;
                PageInfos[i] = new Vector4(drawList[i].drawPos.x, drawList[i].drawPos.y, drawList[i].mip / 255f, 0);
                Materix_MVP[i] = Matrix4x4.TRS(new Vector3(drawList[i].rect.x / pageTexture.PageSize, drawList[i].rect.y / pageTexture.PageSize), Quaternion.identity, new Vector3(size, size, size));
            }

            DrawPageTableBlock.Clear();
            DrawPageTableBlock.SetVectorArray("_PageInfo", PageInfos.ToArray());
            DrawPageTableBlock.SetMatrixArray("_Matrix_MVP", Materix_MVP.ToArray());


            CommandBuffer CmdBuffer = CommandBufferPool.Get("DrawPageTable");
            CmdBuffer.SetRenderTarget(pageTexture.PageTableTexture);
            CmdBuffer.DrawMeshInstanced(DrawPageMesh, 0, DrawPageTableMaterial, 0, Materix_MVP.ToArray(), Materix_MVP.Length, DrawPageTableBlock);

            PageInfos.Dispose();
            Materix_MVP.Dispose();
            Graphics.ExecuteCommandBuffer(CmdBuffer);
        }

		public void InvalidatePage(in Vector2Int id)
        {
            if (!ActivePages.TryGetValue(id, out int3 index))
                return;

            PageTable pageTable = PageTable[index.z];
            ref FPage page = ref pageTable.GetPage(index.x, index.y);

            page.Payload.ResetTileIndex();
            ActivePages.Remove(id);
        }
    }
}
