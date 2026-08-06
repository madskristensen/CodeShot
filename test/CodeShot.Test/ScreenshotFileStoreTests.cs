using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodeShot.ToolWindows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeShot.Test
{
    [TestClass]
    public class ScreenshotFileStoreTests
    {
        private string _folder = null!;
        private BitmapSource _snapshot = null!;

        [TestInitialize]
        public void Initialize()
        {
            _folder = Path.Combine(Path.GetTempPath(), "CodeShot.Test." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            _snapshot = BitmapSource.Create(
                1,
                1,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                new byte[] { 30, 20, 10, 255 },
                4);
            _snapshot.Freeze();
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }

        [TestMethod]
        public void Save_CreatesValidPngAtRequestedPath()
        {
            var requestedPath = Path.Combine(_folder, "Widget.png");

            var savedPath = ScreenshotFileStore.Save(_snapshot, requestedPath, overwrite: false);

            Assert.AreEqual(requestedPath, savedPath);
            CollectionAssert.AreEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
                File.ReadAllBytes(savedPath).Take(8).ToArray());
            Assert.AreEqual(0, Directory.GetFiles(_folder, "*.tmp").Length);
        }

        [TestMethod]
        public void Save_UsesNumberedPathWithoutOverwritingExistingFile()
        {
            var requestedPath = Path.Combine(_folder, "Widget.png");
            File.WriteAllText(requestedPath, "original");

            var savedPath = ScreenshotFileStore.Save(_snapshot, requestedPath, overwrite: false);

            Assert.AreEqual(Path.Combine(_folder, "Widget (2).png"), savedPath);
            Assert.AreEqual("original", File.ReadAllText(requestedPath));
        }

        [TestMethod]
        public void Save_OverwritesExistingFileWhenRequested()
        {
            var requestedPath = Path.Combine(_folder, "Widget.png");
            File.WriteAllText(requestedPath, "original");

            var savedPath = ScreenshotFileStore.Save(_snapshot, requestedPath, overwrite: true);

            Assert.AreEqual(requestedPath, savedPath);
            Assert.AreNotEqual("original", File.ReadAllText(requestedPath));
            Assert.AreEqual(0, Directory.GetFiles(_folder, "*.tmp").Length);
        }

        [TestMethod]
        public void Save_RejectsPathWithoutFolder()
        {
            Assert.ThrowsExactly<InvalidOperationException>(
                () => ScreenshotFileStore.Save(_snapshot, "Widget.png", overwrite: false));
        }
    }
}
