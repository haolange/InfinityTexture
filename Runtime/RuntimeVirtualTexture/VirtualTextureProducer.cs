using System;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace Landscape.ProceduralVirtualTexture
{
    internal struct DrawPageInfo : IComparable<DrawPageInfo>
    {
        public int mip;
        public Rect rect;
        public float2 drawPos;

        public int CompareTo(DrawPageInfo target)
        {
            return -(mip.CompareTo(target.mip));
        }
    }

    internal unsafe class FPageProducer
	{
        public FPageTable[] pageTables;

        public Dictionary<int2, int3> activePageMap = new Dictionary<int2, int3>();


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

            pageTables = new FPageTable[pageTexture.MaxMipLevel + 1];
            for (int i = 0; i <= pageTexture.MaxMipLevel; i++)
            {
                pageTables[i] = new FPageTable(i, pageTexture.PageSize);
            }

            DrawPageTableBlock = new MaterialPropertyBlock();
            DrawPageTableMaterial = new Material(Shader.Find("VirtualTexture/DrawPageTable"));
            DrawPageTableMaterial.enableInstancing = true;

            ActivatePage(0, 0, pageTexture.MaxMipLevel);

            drawList = new NativeList<DrawPageInfo>(256, Allocator.Persistent);
        }

        private void LoadPage(in int x, in int y, ref FPage Page)
        {
            if (Page.isNull == true)
                return;

            // 正在加载中,不需要重复请求
            if (Page.payload.pageRequestInfo.isNull == false)
                return;

            // 新建加载请求
            pageRenderer.AllocateRquestInfo(x, y, Page.mipLevel, ref Page.payload.pageRequestInfo);
        }

        private void ActivatePage(in int x, in int y, in int mip)
        {
            if (mip > pageTexture.MaxMipLevel || mip < 0 || x < 0 || y < 0 || x >= pageTexture.PageSize || y >= pageTexture.PageSize)
                return;

            // 找到当前页表
            ref FPage Page = ref pageTables[mip].GetPage(x, y);

            if (Page.isNull == true) { return; }

            if (!Page.payload.isReady)
            {
                LoadPage(x, y, ref Page);

                //向上找到最近的父节点
                /*while(mip < pageTexture.MaxMipLevel && !Page.Payload.IsReady)
                {
                    mip++;
                    Page = ref PageTable[mip].GetPage(x, y);
                }*/
            }

            if (Page.payload.isReady)
            {
                // 激活对应的平铺贴图块
                Page.payload.activeFrame = Time.frameCount;
                pageTexture.SetActive(Page.payload.pageCoord.y * pageTexture.TileNum + Page.payload.pageCoord.x);
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
        }

        public void DrawPageTable()
        {
            // 将页表数据写入页表贴图
            var currentFrame = (byte)Time.frameCount;
            drawList.Clear();

            foreach (var pageCoord in activePageMap)
            {
                FPageTable pageTable = pageTables[pageCoord.Value.z];
                ref FPage page = ref pageTable.GetPage(pageCoord.Value.x, pageCoord.Value.y);

                // 只写入当前帧活跃的页表
                if (page.payload.activeFrame != Time.frameCount)
                    continue;

                int2 rectXY = new int2(page.rect.xMin, page.rect.yMin);
                while (rectXY.x < 0)
                {
                    rectXY.x += pageTexture.PageSize;
                }
                while (rectXY.y < 0)
                {
                    rectXY.y += pageTexture.PageSize;
                }

                drawList.Add(new DrawPageInfo() {
                    rect = new Rect(rectXY.x, rectXY.y, page.rect.width, page.rect.height),
                    mip = page.mipLevel,
                    drawPos = new float2((float)page.payload.pageCoord.x / 255, (float)page.payload.pageCoord.y / 255),
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

        public void Reset()
        {
            for (int i = 0; i <= pageTexture.MaxMipLevel; ++i)
            {
                FPageTable pageTable = pageTables[i];

                for (int j = 0; j < pageTable.cellCount; ++j)
                {
                    for (int k = 0; k < pageTable.cellCount; k++)
                    {
                        ref FPage page = ref pageTable.pageBuffer[j * pageTable.cellCount + k];
                        InvalidatePage(page.payload.pageCoord);
                    }
                }
            }
            activePageMap.Clear();
        }
        
        public void InvalidatePage(in int2 id)
        {
            if (!activePageMap.TryGetValue(id, out int3 index))
                return;

            FPageTable pageTable = pageTables[index.z];
            ref FPage page = ref pageTable.GetPage(index.x, index.y);

            page.payload.ResetTileIndex();
            activePageMap.Remove(id);
        }
        
        public void Dispose()
        {
            for (int i = 0; i < pageTables.Length; ++i)
            {
                ref FPageTable pageTable = ref pageTables[i];
                pageTable.Dispose();
            }
            drawList.Dispose();
            //PageTable.Dispose();
            //activePageMap.Dispose();
        }
    }
}
