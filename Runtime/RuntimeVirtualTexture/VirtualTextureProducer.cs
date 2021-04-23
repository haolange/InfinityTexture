using System;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace Landscape.ProceduralVirtualTexture
{
    internal struct FPageDrawInfo : IComparable<FPageDrawInfo>
    {
        public int mip;
        public FRect rect;
        public float2 drawPos;

        public int CompareTo(FPageDrawInfo target)
        {
            return -(mip.CompareTo(target.mip));
        }
    }

    internal unsafe class FPageProducer
	{
        public NativeArray<FPageTable> pageTables;

        public Dictionary<int2, int3> activePageMap = new Dictionary<int2, int3>();


        private VirtualTextureAsset pageTexture;

        private FPageRenderer pageRenderer;


        private Mesh DrawPageMesh;

        private Material DrawPageTableMaterial;

        private MaterialPropertyBlock DrawPageTableBlock;

        NativeList<FPageDrawInfo> drawList;


        public void Initialize(Mesh InDrawPageMesh, FPageRenderer InPageRenderer, VirtualTextureAsset InPageTexture)
        {
            DrawPageMesh = InDrawPageMesh;
            pageTexture = InPageTexture;
            pageRenderer = InPageRenderer;

            pageTables = new NativeArray<FPageTable>(pageTexture.MaxMip, Allocator.Persistent);

            for (int i = 0; i < pageTexture.MaxMip; ++i)
            {
                pageTables[i] = new FPageTable(i, pageTexture.pageSize);
            }

            DrawPageTableBlock = new MaterialPropertyBlock();
            DrawPageTableMaterial = new Material(Shader.Find("VirtualTexture/DrawPageTable"));
            DrawPageTableMaterial.enableInstancing = true;

            //ActivatePage(0, 0, pageTexture.MaxMipLevel);

            drawList = new NativeList<FPageDrawInfo>(256, Allocator.Persistent);
        }

        public void ProcessFeedback(in NativeArray<Color32> FeedbackData)
        {
            // 激活对应页表
            for (int i = 0; i < FeedbackData.Length; ++i)
            {
                Color32 Feedback = FeedbackData[i];
                FVirtualTextureUtility.ActivatePage(Feedback.r, Feedback.g, Feedback.b, pageTexture.MaxMip - 1, Time.frameCount, pageTexture.pageSize, pageTexture.tileNum, ref pageTexture.lruCache, pageTables, pageRenderer.pageRequests);
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
                if (page.payload.activeFrame != Time.frameCount) { continue; }

                int2 rectXY = new int2(page.rect.xMin, page.rect.yMin);
                while (rectXY.x < 0)
                {
                    rectXY.x += pageTexture.pageSize;
                }
                while (rectXY.y < 0)
                {
                    rectXY.y += pageTexture.pageSize;
                }

                FPageDrawInfo drawInfo;
                drawInfo.mip = page.mipLevel;
                drawInfo.rect = new FRect(rectXY.x, rectXY.y, page.rect.width, page.rect.height);
                drawInfo.drawPos = new float2((float)page.payload.pageCoord.x / 255, (float)page.payload.pageCoord.y / 255);
                drawList.Add(drawInfo);
            }

            if (drawList.Length == 0) { return; }
            drawList.Sort();

            NativeArray<Vector4> PageInfos = new NativeArray<Vector4>(drawList.Length, Allocator.TempJob);
            NativeArray<Matrix4x4> Materix_MVP = new NativeArray<Matrix4x4>(drawList.Length, Allocator.TempJob);

            for (int i = 0; i < drawList.Length; ++i)
            {
                float size = drawList[i].rect.width / pageTexture.pageSize;
                PageInfos[i] = new Vector4(drawList[i].drawPos.x, drawList[i].drawPos.y, drawList[i].mip / 255f, 0);
                Materix_MVP[i] = Matrix4x4.TRS(new Vector3(drawList[i].rect.x / pageTexture.pageSize, drawList[i].rect.y / pageTexture.pageSize), Quaternion.identity, new Vector3(size, size, size));
            }

            DrawPageTableBlock.Clear();
            DrawPageTableBlock.SetVectorArray("_PageInfo", PageInfos.ToArray());
            DrawPageTableBlock.SetMatrixArray("_Matrix_MVP", Materix_MVP.ToArray());

            CommandBuffer CmdBuffer = CommandBufferPool.Get("DrawPageTable");
            CmdBuffer.SetRenderTarget(pageTexture.pageTableTexture);
            CmdBuffer.DrawMeshInstanced(DrawPageMesh, 0, DrawPageTableMaterial, 0, Materix_MVP.ToArray(), Materix_MVP.Length, DrawPageTableBlock);

            PageInfos.Dispose();
            Materix_MVP.Dispose();
            Graphics.ExecuteCommandBuffer(CmdBuffer);
        }

        public void Reset()
        {
            for (int i = 0; i < pageTables.Length; ++i)
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
                FPageTable pageTable = pageTables[i];
                pageTable.Dispose();
            }
            drawList.Dispose();
            pageTables.Dispose();
            //activePageMap.Dispose();
        }
    }
}
