using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;

namespace Landscape.ProceduralVirtualTexture
{
    public class FeedbackRenderer
	{
        public float MipmapBias = 0.5f;

        public Camera FeedbackCamera { get; set; }

        public RenderTexture TargetTexture { get; private set; }


        public void Initialize(Camera InMainCamera, Camera InFeedbackCamera, int2 InFeedbackSize, FeedbackScale InFeedbackScale, FPageProducer InPageProducer, RuntimeVirtualTexture InPageTexture)
		{
            FeedbackCamera = InFeedbackCamera;
            FeedbackCamera.enabled = false;

            // 处理屏幕尺寸变换
            float scale = InFeedbackScale.ToFloat();
            int width = (int)(InFeedbackSize.x * scale);
            int height = (int)(InFeedbackSize.y * scale);

            if (TargetTexture == null || TargetTexture.width != width || TargetTexture.height != height)
            {
                TargetTexture = new RenderTexture(width, height, 0, GraphicsFormat.R8G8B8A8_UNorm);
                TargetTexture.name = "FeedbackTexture";
                TargetTexture.useMipMap = false;
                TargetTexture.wrapMode = TextureWrapMode.Clamp;
                TargetTexture.filterMode = FilterMode.Point;

                FeedbackCamera.targetTexture = TargetTexture;


                // 设置预渲染着色器参数
                // x: 页表大小(单位: 页)
                // y: 虚拟贴图大小(单位: 像素)
                // z: 最大mipmap等级
                Shader.SetGlobalVector("_VTFeedbackParam", new Vector4(InPageTexture.PageSize, InPageTexture.PageSize * InPageTexture.TileSize * scale, InPageTexture.MaxMipLevel - 1, MipmapBias));
            }

            // 渲染前先拷贝主摄像机的相关参数
            CopyCamera(InMainCamera);
		}

		private void CopyCamera(Camera camera)
		{
			if(camera == null)
				return;

			FeedbackCamera.fieldOfView = camera.fieldOfView + 30;
			FeedbackCamera.nearClipPlane = camera.nearClipPlane;
			FeedbackCamera.farClipPlane = camera.farClipPlane;
		}
    }
}
