using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace Landscape.ProceduralVirtualTexture
{
    public class FPageProducer
	{
        public PageTable[] PageTable;

        public Dictionary<Vector2Int, FPage> ActivePages = new Dictionary<Vector2Int, FPage>();


        private RuntimeVirtualTexture pageTexture;

        private FPageRenderer pageRenderer;


        private Mesh DrawPageMesh;

        private Material DrawPageTableMaterial;

        private MaterialPropertyBlock DrawPageTableBlock;



        public void Initialize(Mesh InDrawPageMesh, FPageRenderer InPageRenderer, RuntimeVirtualTexture InPageTexture)
        {
            DrawPageMesh = InDrawPageMesh;
            pageTexture = InPageTexture;
            pageRenderer = InPageRenderer;

            PageTable = new PageTable[pageTexture.MaxMipLevel + 1];
            for (int i = 0; i <= pageTexture.MaxMipLevel; ++i)
            {
                PageTable[i] = new PageTable(i, pageTexture.PageSize);
            }

            DrawPageTableBlock = new MaterialPropertyBlock();
            DrawPageTableMaterial = new Material(Shader.Find("VirtualTexture/DrawPageTable"));
            DrawPageTableMaterial.enableInstancing = true;

            ActivatePage(0, 0, pageTexture.MaxMipLevel);
        }

        public void Reset()
        {
            for (int i = 0; i <= pageTexture.MaxMipLevel; i++)
            {
                for(int j=0;j<PageTable[i].NodeCellCount;j++)
                    for (int k = 0; k < PageTable[i].NodeCellCount; k++)
                    {
                        InvalidatePage(PageTable[i].PageBuffer[j,k].Payload.TileIndex);
                    }
            }
            ActivePages.Clear();
        }

        public void ProcessFeedback(NativeArray<Color32> FeedbackData)
        {
            // 激活对应页表
            foreach (Color32 Feedback in FeedbackData)
            {
                ActivatePage(Feedback.r, Feedback.g, Feedback.b);
            }
            
            DrawPageTable();
        }

        private struct DrawPageInfo
        {
            public Rect rect;
            public int mip;
            public Vector2 drawPos;
        }

        private void DrawPageTable()
        {
            // 将页表数据写入页表贴图
            var currentFrame = (byte)Time.frameCount;
            var drawList = new List<DrawPageInfo>();
            foreach (var kv in ActivePages)
            {
                var page = kv.Value;
                // 只写入当前帧活跃的页表
                if (page.Payload.ActiveFrame != Time.frameCount)
                    continue;

                var table = PageTable[page.MipLevel];
                var offset = table.pageOffset;
                var perSize = table.PerCellSize;
                var lb = new Vector2Int((page.Rect.xMin - offset.x * perSize),
                                          (page.Rect.yMin - offset.y * perSize));
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

            drawList.Sort((a,b) => {
                return -(a.mip.CompareTo(b.mip));
            });

            if(drawList.Count == 0)
            {
                return;
            }

            var PageInfos = new Vector4[drawList.Count];
            var Materix_MVP = new Matrix4x4[drawList.Count];
            for(int i=0;i<drawList.Count;i++)
            {
                float size = drawList[i].rect.width / pageTexture.PageSize;
                Materix_MVP[i] = Matrix4x4.TRS(new Vector3(drawList[i].rect.x / pageTexture.PageSize, drawList[i].rect.y / pageTexture.PageSize), 
                                        Quaternion.identity,
                                        new Vector3(size, size, size));

                PageInfos[i] = new Vector4(drawList[i].drawPos.x, drawList[i].drawPos.y, drawList[i].mip / 255f,0);
            }

            DrawPageTableBlock.Clear();
            DrawPageTableBlock.SetVectorArray("_PageInfo", PageInfos);
            DrawPageTableBlock.SetMatrixArray("_Matrix_MVP", Materix_MVP);

            CommandBuffer CmdBuffer = CommandBufferPool.Get("DrawPageTable");
            CmdBuffer.SetRenderTarget(pageTexture.PageTableTexture);
            CmdBuffer.DrawMeshInstanced(DrawPageMesh, 0, DrawPageTableMaterial, 0, Materix_MVP, Materix_MVP.Length, DrawPageTableBlock);
            Graphics.ExecuteCommandBuffer(CmdBuffer);
        }

        private void LoadPage(in int x, in int y, FPage Page)
        {
            if (Page == null)
                return;

            // 正在加载中,不需要重复请求
            if (Page.Payload.pageRequestInfo.bNull == false)
                return;

            // 新建加载请求
            Page.Payload.pageRequestInfo = pageRenderer.AllocateRquestInfo(x, y, Page.MipLevel);
        }

        private void ActivatePage(in int x, in int y, int mip)
        {
            if (mip > pageTexture.MaxMipLevel || mip < 0 || x < 0 || y < 0 || x >= pageTexture.PageSize || y >= pageTexture.PageSize)
                return;

            // 找到当前页表
            FPage Page = PageTable[mip].GetPage(x, y);

            if(Page == null) { return; }

            if(!Page.Payload.IsReady)
            {
                LoadPage(x, y, Page);

                //向上找到最近的父节点
                while(mip < pageTexture.MaxMipLevel && !Page.Payload.IsReady)
                {
                    mip++;
                    Page = PageTable[mip].GetPage(x, y);
                }
            }

            if (Page.Payload.IsReady)
            {
                // 激活对应的平铺贴图块
                pageTexture.SetActive(Page.Payload.TileIndex);
                Page.Payload.ActiveFrame = Time.frameCount;
                return;
            }

            return;
        }

		public void InvalidatePage(in Vector2Int id)
        {
            if (!ActivePages.TryGetValue(id, out var node))
                return;

            node.Payload.ResetTileIndex();
            ActivePages.Remove(id);
        }
    }
}
