using System;
using System.IO;
using CodeShot.ToolWindows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeShot.Test
{
    [TestClass]
    public class ScreenshotFileNamingTests
    {
        private string _folder = null!;

        [TestInitialize]
        public void Initialize()
        {
            _folder = Path.Combine(Path.GetTempPath(), "CodeShot.Test." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
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
        public void GetUniquePath_ReturnsRequestedNameWhenAvailable()
        {
            var path = ScreenshotFileStore.GetUniquePath(_folder, "Widget");

            Assert.AreEqual(Path.Combine(_folder, "Widget.png"), path);
        }

        [TestMethod]
        public void GetUniquePath_UsesFirstAvailableNumberedName()
        {
            File.WriteAllText(Path.Combine(_folder, "Widget.png"), string.Empty);
            File.WriteAllText(Path.Combine(_folder, "Widget (2).png"), string.Empty);
            File.WriteAllText(Path.Combine(_folder, "Widget (3).png"), string.Empty);

            var path = ScreenshotFileStore.GetUniquePath(_folder, "Widget");

            Assert.AreEqual(Path.Combine(_folder, "Widget (4).png"), path);
        }

        [TestMethod]
        public void GetUniquePath_DoesNotReserveTheReturnedName()
        {
            var path = ScreenshotFileStore.GetUniquePath(_folder, "Widget");

            Assert.IsFalse(File.Exists(path));
        }
    }
}
