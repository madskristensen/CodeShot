using System.Drawing;
using CodeShot.ToolWindows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeShot.Test
{
    [TestClass]
    public class ToolWindowCaptureTests
    {
        [DataTestMethod]
        [DataRow(0, 100)]
        [DataRow(100, 0)]
        [DataRow(-1, 100)]
        [DataRow(100, -1)]
        [DataRow(16385, 1)]
        [DataRow(1, 16385)]
        [DataRow(16001, 2000)]
        public void IsCaptureSizeSupported_RejectsInvalidOrOversizedDimensions(int width, int height)
        {
            Assert.IsFalse(ToolWindowCapture.IsCaptureSizeSupported(new Rectangle(-1920, -1080, width, height)));
        }

        [DataTestMethod]
        [DataRow(1, 1)]
        [DataRow(16384, 1)]
        [DataRow(1, 16384)]
        [DataRow(16000, 2000)]
        [DataRow(16384, 1953)]
        public void IsCaptureSizeSupported_AcceptsValuesAtLimits(int width, int height)
        {
            Assert.IsTrue(ToolWindowCapture.IsCaptureSizeSupported(new Rectangle(-1920, -1080, width, height)));
        }

        [TestMethod]
        public void IsCaptureSizeSupported_RejectsFirstPixelCountAboveLimit()
        {
            Assert.IsFalse(ToolWindowCapture.IsCaptureSizeSupported(new Rectangle(0, 0, 16384, 1954)));
        }
    }
}
