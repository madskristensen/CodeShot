using System.IO;
using System.Windows.Media.Imaging;

namespace CodeShot.ToolWindows
{
    internal static class PngImageEncoder
    {
        internal static MemoryStream Encode(BitmapSource snapshot)
        {
            var output = new MemoryStream();
            Encode(snapshot, output);
            output.Position = 0;
            return output;
        }

        internal static void Encode(BitmapSource snapshot, Stream output)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(snapshot));
            encoder.Save(output);
        }
    }
}
