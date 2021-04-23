using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;

namespace Landscape.ProceduralVirtualTexture
{
    internal class FeedbackRenderer
	{
        public float MipmapBias = 0.0f;

        public Camera FeedbackCamera { get; set; }

        public RenderTexture TargetTexture { get; private set; }


        public void Initialize(Camera InMainCamera, Camera InFeedbackCamera, int2 InFeedbackSize, FeedbackScale InFeedbackScale, FPageProducer InPageProducer, VirtualTextureAsset InPageTexture)
		{
            FeedbackCamera = InFeedbackCamera;
            FeedbackCamera.enabled = false;

            // 处理屏幕尺寸变换
            float scale = InFeedbackScale.ToFloat();
            int width = (int)(InFeedbackSize.x * scale);
            int height = (int)(InFeedbackSize.y * scale);

            if (TargetTexture == null || TargetTexture.width != width || TargetTexture.height != height)
            {
                TargetTexture = RenderTexture.GetTemporary(width, height, 1, GraphicsFormat.R8G8B8A8_UNorm, 1);
                TargetTexture.name = "FeedbackTexture";

                FeedbackCamera.targetTexture = TargetTexture;


                // 设置预渲染着色器参数
                // x: 页表大小(单位: 页)
                // y: 虚拟贴图大小(单位: 像素)
                // z: 最大mipmap等级
                Shader.SetGlobalVector("_VTFeedbackParam", new Vector4(InPageTexture.pageSize, InPageTexture.pageSize * InPageTexture.tileSize * scale, InPageTexture.MaxMip, MipmapBias));
            }

            // 渲染前先拷贝主摄像机的相关参数
            CopyCamera(InMainCamera);
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
