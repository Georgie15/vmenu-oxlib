using System;
using System.Collections.Generic;

namespace vMenuClient
{
    internal static class FaceFeatureValues
    {
        internal static float GetValue(IDictionary<int, float> features, int index)
        {
            if (features == null || !features.TryGetValue(index, out float value) || float.IsNaN(value))
            {
                return 0f;
            }
            return Math.Max(-1f, Math.Min(1f, value));
        }

        internal static int GetSliderPosition(IDictionary<int, float> features, int index)
        {
            return (int)Math.Round((GetValue(features, index) + 1f) * 10f);
        }
    }
}
