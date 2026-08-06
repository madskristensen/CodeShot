using System;

namespace CodeShot.ToolWindows
{
    internal static class TextCaptureWidth
    {
        internal const double Minimum = 320;

        internal static double Clamp(double value)
            => double.IsNaN(value) || double.IsInfinity(value)
                ? Minimum
                : Math.Max(Minimum, value);

        internal static double AddDelta(double? pendingWidth, double currentWidth, double delta)
            => Clamp((pendingWidth ?? currentWidth) + delta);
    }
}
