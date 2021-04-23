using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;

namespace Landscape.RuntimeVirtualTexture
{
    internal class FeedbackRenderer
	{
        public float mipmapBias = 0.0f;
        public Camera FeedbackCamera { get; set; }
        public RenderTexture TargetTexture { get; private set; }


        public void Initialize(Camera mainCamera, Camera feedbackCamera, int2 feedbackSize, FeedbackScale feedbackScale, VirtualTextureAsset virtualTexture)
		{
            FeedbackCamera = feedbackCamera;
            FeedbackCamera.enabled = false;

            // 处理屏幕尺寸变换
            float scale = feedbackScale.ToFloat();
            int width = (int)(feedbackSize.x * scale);
            int height = (int)(feedbackSize.y * scale);

            if (TargetTexture == null || TargetTexture.width != width || TargetTexture.height != height)
            {
                TargetTexture = RenderTexture.GetTemporary(width, height, 1, GraphicsFormat.R8G8B8A8_UNorm, 1);
                TargetTexture.name = "FeedbackTexture";

                FeedbackCamera.targetTexture = TargetTexture;


                // 设置预渲染着色器参数
                // x: 页表大小(单位: 页)
                // y: 虚拟贴图大小(单位: 像素)
                // z: 最大mipmap等级
                Shader.SetGlobalVector("_VTFeedbackParam", new Vector4(virtualTexture.pageSize, virtualTexture.pageSize * virtualTexture.tileSize * scale, virtualTexture.MaxMip, mipmapBias));
            }

            // 渲染前先拷贝主摄像机的相关参数
            CopyCamera(mainCamera);
		}

        public void Release()
        {
            RenderTexture.ReleaseTemporary(TargetTexture);
        }

        private void CopyCamera(Camera camera)
		{
			if(camera == null)
				return;

			FeedbackCamera.fieldOfView = camera.fieldOfView;
			FeedbackCamera.nearClipPlane = camera.nearClipPlane;
			FeedbackCamera.farClipPlane = camera.farClipPlane;
		}
    }
}
