using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CodeShot
{
    internal partial class OptionsProvider
    {
        [ComVisible(true)]
        public class GeneralOptions : BaseOptionPage<General> { }
    }

    public class General : BaseOptionModel<General>
    {
        [Category("Font")]
        [DisplayName("Font family")]
        [Description("The font used in the screenshot. Leave empty to use the font from the text editor.")]
        [DefaultValue("")]
        [TypeConverter(typeof(FontFamilyTypeConverter))]
        public string FontFamily { get; set; } = string.Empty;

        [Category("Font")]
        [DisplayName("Font size")]
        [Description("The font size used in the screenshot. Set to 0 to use the font size from the text editor.")]
        [DefaultValue(0d)]
        [TypeConverter(typeof(FontSizeTypeConverter))]
        public double FontSize { get; set; }

        [Category("Appearance")]
        [DisplayName("Show line numbers")]
        [Description("Show line numbers to the left of the code in the screenshot.")]
        [DefaultValue(true)]
        public bool ShowLineNumbers { get; set; } = true;

        [Category("Appearance")]
        [DisplayName("Real line numbers")]
        [Description("Number the lines from their position in the file instead of starting at 1.")]
        [DefaultValue(false)]
        public bool UseRealLineNumbers { get; set; }

        [Category("Appearance")]
        [DisplayName("Show title bar")]
        [Description("Show the title bar with the file name at the top of the screenshot.")]
        [DefaultValue(true)]
        public bool ShowTitleBar { get; set; } = true;

        [Category("Export")]
        [DisplayName("Export scale")]
        [Description("Resolution multiplier for the exported image. Higher values produce sharper images at a larger pixel size. The result is identical on every monitor regardless of its DPI.")]
        [DefaultValue(2d)]
        [TypeConverter(typeof(ExportScaleTypeConverter))]
        public double ExportScale { get; set; } = 2d;

        [Category("Export")]
        [DisplayName("Copy plain text with image")]
        [Description("Also place the selected code on the clipboard as plain text. The image is still pasted by apps that accept images, while editors and chat clients that prefer text receive code that can be copied and searched.")]
        [DefaultValue(true)]
        public bool CopyPlainTextWithImage { get; set; } = true;
    }
}
