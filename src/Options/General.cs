using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CodeShot
{
    public enum BackgroundMode
    {
        Theme,
        Custom,
        Transparent
    }

    public enum WindowControls
    {
        None,
        MacOs,
        Windows
    }

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
        [DisplayName("Window controls")]
        [Description("The window buttons drawn in the title bar. None shows the file name on its own, MacOs draws the three colored dots, and Windows draws the minimize, maximize and close glyphs.")]
        [DefaultValue(WindowControls.Windows)]
        public WindowControls WindowControls { get; set; } = WindowControls.Windows;

        [Category("Appearance")]
        [DisplayName("Background")]
        [Description("Theme derives the background from the editor colors, Custom uses the background color below, and Transparent leaves it empty so the screenshot blends into any surface. Transparency is preserved when saving to a PNG file.")]
        [DefaultValue(BackgroundMode.Theme)]
        public BackgroundMode BackgroundMode { get; set; } = BackgroundMode.Theme;

        [Category("Appearance")]
        [DisplayName("Background color")]
        [Description("The background color used when Background is set to Custom, written as a hex value such as #ABB8C3.")]
        [DefaultValue("#ABB8C3")]
        public string BackgroundColor { get; set; } = "#ABB8C3";

        [Category("Appearance")]
        [DisplayName("Padding")]
        [Description("The space in pixels between the code window and the edge of the screenshot. Set to 0 to export the code window on its own.")]
        [DefaultValue(18)]
        public int Padding { get; set; } = 18;

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
