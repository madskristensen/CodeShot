using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace CodeShot.ToolWindows
{
    internal static class ScreenshotFileStore
    {
        internal static string Save(BitmapSource snapshot, string path, bool overwrite)
        {
            var folder = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(folder))
            {
                throw new InvalidOperationException("The save path does not contain a folder.");
            }

            var fileName = Path.GetFileNameWithoutExtension(path);
            var temporaryPath = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    PngImageEncoder.Encode(snapshot, stream);
                    stream.Flush(true);
                }

                if (overwrite)
                {
                    if (File.Exists(path))
                    {
                        File.Replace(temporaryPath, path, null);
                    }
                    else
                    {
                        File.Move(temporaryPath, path);
                    }

                    return path;
                }

                for (var attempt = 0; attempt < 10; attempt++)
                {
                    var candidate = GetUniquePath(folder, fileName);

                    try
                    {
                        File.Move(temporaryPath, candidate);
                        return candidate;
                    }
                    catch (IOException ex) when (File.Exists(candidate))
                    {
                        _ = ex.LogAsync();
                    }
                }

                throw new IOException("Could not reserve a unique screenshot file name.");
            }
            finally
            {
                DeleteTemporaryFile(temporaryPath);
            }
        }

        internal static string GetUniquePath(string folder, string fileName)
        {
            var candidate = Path.Combine(folder, fileName + ".png");

            for (var attempt = 2; File.Exists(candidate) && attempt <= 1000; attempt++)
            {
                candidate = Path.Combine(folder, $"{fileName} ({attempt}).png");
            }

            if (File.Exists(candidate))
            {
                candidate = Path.Combine(folder, $"{fileName} ({Guid.NewGuid():N}).png");
            }

            return candidate;
        }

        private static void DeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
            }
        }
    }
}
