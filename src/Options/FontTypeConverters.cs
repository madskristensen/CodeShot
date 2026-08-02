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
}
