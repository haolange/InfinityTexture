namespace Landscape.RuntimeVirtualTexture
{
	public enum FeedbackScale
	{
		X1,
        X2,
        X4,
        X8,
		X16
	}

	public static class ScaleModeExtensions
	{
		public static float ToFloat(this FeedbackScale mode)
		{
			switch(mode)
			{
                case FeedbackScale.X16:
                    return 0.0625f;

                case FeedbackScale.X8:
                    return 0.125f;

                case FeedbackScale.X4:
                    return 0.25f;

                case FeedbackScale.X2:
                    return 0.5f;
            }
			return 1;
		}
	}
}