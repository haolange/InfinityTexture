using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;

namespace Landscape.RuntimeVirtualTexture
{
    public enum EFeedbackScale
    {
        X1 = 1,
        X2 = 2,
        X4 = 4,
        X8 = 8,
        X16 = 16
    }

    internal class FeedbackRenderer : IDisposable
    {
        public float mipmapBias = 0.0f;
        public Camera FeedbackCamera { get; set; }
        public RenderTexture TargetTexture { get; private set; }


        public void Initialize(Camera mainCamera, Camera feedbackCamera, int2 feedbackSize, EFeedbackScale feedbackScale, VirtualTextureAsset virtualTexture)
		{
            FeedbackCamera = feedbackCamera;
            FeedbackCamera.enabled = false;

            int width = (feedbackSize.x / (int)feedbackScale);
            int height = (feedbackSize.y / (int)feedbackScale);
            if (TargetTexture == null || TargetTexture.width != width || TargetTexture.height != height)
            {
                TargetTexture = RenderTexture.GetTemporary(width, height, 1, GraphicsFormat.R8G8B8A8_UNorm, 1);
                TargetTexture.name = "FeedbackTexture";
                FeedbackCamera.targetTexture = TargetTexture;

                // 设置预渲染着色器参数
                // x: 页表大小(单位: 页)
                // y: 虚拟贴图大小(单位: 像素)
                // z: 最大mipmap等级
                Shader.SetGlobalVector("_VTFeedbackParams", new Vector4(virtualTexture.pageSize, virtualTexture.pageSize * virtualTexture.tileSize * (1.0f / (float)feedbackScale), virtualTexture.NumMip, mipmapBias));
            }

            CopyCameraParameter(mainCamera);
		}

        private void CopyCameraParameter(Camera camera)
        {
            if (camera == null)
                return;

            FeedbackCamera.fieldOfView = camera.fieldOfView;
            FeedbackCamera.nearClipPlane = camera.nearClipPlane;
            FeedbackCamera.farClipPlane = camera.farClipPlane;
        }

        public void Dispose()
        {
            RenderTexture.ReleaseTemporary(TargetTexture);
        }
    }
}
