using CodeShot.ToolWindows;
using System.ComponentModel;
using System.Linq;

namespace CodeShot
{
    internal sealed class FontFamilyTypeConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;

        // Not exclusive, so the value can still be cleared to follow the text editor font.
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => false;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            => new StandardValuesCollection(FontCatalog.Families.ToArray());
    }

    internal sealed class FontSizeTypeConverter : DoubleConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;

        // Not exclusive, so any custom size can still be typed.
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => false;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            => new StandardValuesCollection(FontCatalog.Sizes.ToArray());
    }

    internal sealed class LineHeightTypeConverter : DoubleConverter
    {
        private static readonly object[] Multipliers = { 1d, 1.15d, 1.3d, 1.45d, 1.6d, 1.8d, 2d };

        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;

        // Not exclusive, so any custom multiplier can still be typed.
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => false;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            => new StandardValuesCollection(Multipliers);
    }
}
