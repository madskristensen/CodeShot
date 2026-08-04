using System.Windows.Media.Imaging;

namespace CodeShot.ToolWindows
{
    internal sealed class ToolWindowSnapshot
    {
        internal ToolWindowSnapshot(BitmapSource image, string caption)
        {
            Image = image;
            Caption = caption;
        }

        internal BitmapSource Image { get; }
        internal string Caption { get; }
    }
}
