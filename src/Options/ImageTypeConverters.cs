using System.ComponentModel;

namespace CodeShot
{
    internal sealed class ExportScaleTypeConverter : DoubleConverter
    {
        private static readonly object[] Scales = { 1d, 1.5d, 2d, 3d, 4d };

        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;

        // Not exclusive, so any custom scale can still be typed.
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => false;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            => new StandardValuesCollection(Scales);
    }

    internal sealed class GradientAngleTypeConverter : Int32Converter
    {
        private static readonly object[] Angles = { 0, 45, 90, 135, 180, 225, 270, 315 };

        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;

        // Not exclusive, so any custom angle can still be typed.
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => false;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            => new StandardValuesCollection(Angles);
    }
}
